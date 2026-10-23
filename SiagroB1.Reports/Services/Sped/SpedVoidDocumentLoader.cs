using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;

namespace SiagroB1.Reports.Services.Sped;

/// <summary>NF-e próprias canceladas/denegadas/inutilizadas do mês, comuns ao SPED Fiscal e à EFD Contribuições.</summary>
public static class SpedVoidDocumentLoader
{
    /// <summary>
    /// NF-e própria do mês com situação cancelada (02), denegada (04) ou inutilizada (05). Não filtra
    /// pela situação do documento: a NF-e cancelada costuma ter o documento cancelado também.
    /// </summary>
    public static async Task<List<SpedVoidDocument>> LoadAsync(IUnitOfWork db, string branchCode, int year, int month)
    {
        var from = new DateTime(year, month, 1);
        var toExclusive = from.AddMonths(1);
        NfeStatus[] statuses = [NfeStatus.Cancelled, NfeStatus.Denied, NfeStatus.Voided];

        var sales = await db.Context.SalesInvoices.AsNoTracking()
            .Where(s => s.BranchCode == branchCode && s.TaxDocumentKind == TaxDocumentKind.Nfe)
            .Where(s => statuses.Contains(s.NfeStatus))
            .Where(s => s.InvoiceDate >= from && s.InvoiceDate < toExclusive)
            .Select(s => new
            {
                s.NfeStatus, s.TaxDocumentSeries, s.TaxDocumentNumber, s.InvoiceNumber, s.ChaveNFe,
                IsReturn = s.InvoiceType == SalesInvoiceType.Return,
                Cfop = s.Items.OrderBy(i => i.NfeItemNumber).Select(i => i.Cfop).FirstOrDefault(),
            })
            .ToListAsync();

        var purchases = await db.Context.PurchaseInvoices.AsNoTracking()
            .Where(p => p.BranchCode == branchCode && p.TaxDocumentKind == TaxDocumentKind.Nfe)
            .Where(p => p.IssuerType == DocumentIssuerType.Own)
            .Where(p => statuses.Contains(p.NfeStatus))
            .Where(p => (p.PostingDate ?? p.IssueDate) >= from && (p.PostingDate ?? p.IssueDate) < toExclusive)
            .Select(p => new
            {
                p.NfeStatus, p.TaxDocumentSeries, p.TaxDocumentNumber, p.InvoiceNumber, p.ChaveNFe,
                IsReturn = p.InvoiceType == PurchaseInvoiceType.Return,
                Cfop = p.Items.OrderBy(i => i.NfeItemNumber).Select(i => i.Cfop).FirstOrDefault(),
            })
            .ToListAsync();

        // IND_OPER pelo sentido da operação, como no C100 regular: CFOP da 1ª linha; sem CFOP, o tipo
        // (devolução de venda é entrada, devolução de compra própria é saída)
        return sales.Select(s => Void(Direction(s.Cfop, s.IsReturn ? "0" : "1"), s.NfeStatus, s.TaxDocumentSeries, s.TaxDocumentNumber ?? s.InvoiceNumber, s.ChaveNFe))
            .Concat(purchases.Select(p => Void(Direction(p.Cfop, p.IsReturn ? "1" : "0"), p.NfeStatus, p.TaxDocumentSeries, p.TaxDocumentNumber ?? p.InvoiceNumber, p.ChaveNFe)))
            .OrderBy(v => v.Direction, StringComparer.Ordinal)
            .ThenBy(v => v.Series, StringComparer.Ordinal)
            .ThenBy(v => v.Number, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>"0" entrada / "1" saída pelo 1º dígito do CFOP; sem CFOP válido, o padrão do tipo.</summary>
    private static string Direction(string? cfop, string fallback) => cfop?.Trim() switch
    {
        [var first, ..] when first is '1' or '2' or '3' => "0",
        [var first, ..] when first is '5' or '6' or '7' => "1",
        _ => fallback,
    };

    private static SpedVoidDocument Void(string direction, NfeStatus status, string? series, string? number, string? key) => new()
    {
        Direction = direction,
        Situation = status switch
        {
            NfeStatus.Cancelled => "02",
            NfeStatus.Denied => "04",
            _ => "05",
        },
        Model = "55",
        Series = series ?? "",
        Number = number ?? "",
        AccessKey = key,
    };
}
