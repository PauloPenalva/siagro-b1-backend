using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;

namespace SiagroB1.Reports.Services;

/// <summary>
/// Conferência Fiscal do mês (equivale à conferência do SPED Fiscal e do R-2055): lista os
/// documentos escrituráveis item a item. Lê os tributos gravados na linha — nunca recalcula.
/// </summary>
public class FiscalConferenceService(IUnitOfWork db, FunruralWithholdingReportService funrural)
{
    // Reservado para a aba R-2055 (Task 4).
    private readonly FunruralWithholdingReportService _funrural = funrural;

    /// <summary>NF-e que não vale como documento escriturável.</summary>
    private static readonly NfeStatus[] VoidNfeStatuses = [NfeStatus.Cancelled, NfeStatus.Denied, NfeStatus.Voided];

    private static readonly InvoiceStatus[] BookableStatuses = [InvoiceStatus.Confirmed, InvoiceStatus.Returned];

    private static readonly TaxDocumentKind[] BookableKinds = [TaxDocumentKind.Nfe, TaxDocumentKind.Other];

    public async Task<FiscalConferenceData> BuildAsync(FiscalConferenceRequest request)
    {
        var (included, _) = await LoadLinesAsync(request);
        var (assessment, totals) = BuildAssessment(included);
        return new FiscalConferenceData
        {
            Documents = included,
            Summary = BuildSummary(included),
            Assessment = assessment,
            AssessmentTotals = totals,
        };
    }

    /// <summary>Agrupa por direção, CST, CFOP e alíquota; "E" vem antes de "S".</summary>
    private static List<FiscalSummaryRowDto> BuildSummary(List<FiscalDocumentLineDto> lines) => lines
        .GroupBy(l => (l.Direction, l.CstIcms, l.Cfop, l.IcmsRate))
        .Select(g => new FiscalSummaryRowDto
        {
            Direction = g.Key.Direction,
            CstIcms = g.Key.CstIcms,
            Cfop = g.Key.Cfop,
            IcmsRate = g.Key.IcmsRate,
            OperationValue = g.Sum(l => l.OperationValue),
            IcmsBase = g.Sum(l => l.IcmsBase),
            IcmsValue = g.Sum(l => l.IcmsValue),
        })
        .OrderBy(r => r.Direction, StringComparer.Ordinal)
        .ThenBy(r => r.Cfop, StringComparer.Ordinal)
        .ThenBy(r => r.CstIcms, StringComparer.Ordinal)
        .ThenBy(r => r.IcmsRate)
        .ToList();

    /// <summary>Saída (CFOP 5/6/7) debita o ICMS; entrada (1/2/3) credita. A direção já vem do CFOP.</summary>
    private static (List<IcmsAssessmentRowDto> Rows, IcmsAssessmentTotalsDto Totals) BuildAssessment(
        List<FiscalDocumentLineDto> lines)
    {
        var rows = lines
            .GroupBy(l => l.Cfop)
            .Select(g => new IcmsAssessmentRowDto
            {
                Cfop = g.Key,
                OperationValue = g.Sum(l => l.OperationValue),
                Debit = g.Where(l => l.Direction == "S").Sum(l => l.IcmsValue),
                Credit = g.Where(l => l.Direction == "E").Sum(l => l.IcmsValue),
            })
            .OrderBy(r => r.Cfop, StringComparer.Ordinal)
            .ToList();

        var debits = rows.Sum(r => r.Debit);
        var credits = rows.Sum(r => r.Credit);
        return (rows, new IcmsAssessmentTotalsDto { Debits = debits, Credits = credits, Balance = debits - credits });
    }

    /// <summary>
    /// Linhas escrituráveis do mês e, à parte, as de NF-e PRÓPRIA ainda não autorizada (ficam fora
    /// dos totais e viram inconsistência). Entrada de terceiro não depende da autorização.
    /// </summary>
    public async Task<(List<FiscalDocumentLineDto> Included, List<FiscalDocumentLineDto> Unauthorized)> LoadLinesAsync(
        FiscalConferenceRequest request)
    {
        var from = new DateTime(request.Year, request.Month, 1);
        var toExclusive = from.AddMonths(1);
        var branch = string.IsNullOrWhiteSpace(request.BranchCode) ? null : request.BranchCode;

        var sales = db.Context.SalesInvoicesItems
            .AsNoTracking()
            .Include(i => i.SalesInvoice)
            .Where(i => i.SalesInvoice != null)
            .Where(i => i.SalesInvoice!.InvoiceStatus != null && BookableStatuses.Contains(i.SalesInvoice.InvoiceStatus.Value))
            .Where(i => !i.SalesInvoice!.WithoutTaxDocument)
            .Where(i => BookableKinds.Contains(i.SalesInvoice!.TaxDocumentKind))
            .Where(i => !VoidNfeStatuses.Contains(i.SalesInvoice!.NfeStatus))
            .Where(i => i.SalesInvoice!.InvoiceDate >= from && i.SalesInvoice.InvoiceDate < toExclusive);

        var purchases = db.Context.PurchaseInvoicesItems
            .AsNoTracking()
            .Include(i => i.PurchaseInvoice)
            .Where(i => i.PurchaseInvoice != null)
            .Where(i => BookableStatuses.Contains(i.PurchaseInvoice!.InvoiceStatus))
            .Where(i => BookableKinds.Contains(i.PurchaseInvoice!.TaxDocumentKind))
            .Where(i => !VoidNfeStatuses.Contains(i.PurchaseInvoice!.NfeStatus))
            .Where(i => (i.PurchaseInvoice!.PostingDate ?? i.PurchaseInvoice.IssueDate) >= from
                     && (i.PurchaseInvoice.PostingDate ?? i.PurchaseInvoice.IssueDate) < toExclusive);

        if (branch is not null)
        {
            sales = sales.Where(i => i.SalesInvoice!.BranchCode == branch);
            purchases = purchases.Where(i => i.PurchaseInvoice!.BranchCode == branch);
        }

        var included = new List<FiscalDocumentLineDto>();
        var unauthorized = new List<FiscalDocumentLineDto>();

        foreach (var item in await sales.ToListAsync())
        {
            var invoice = item.SalesInvoice!;
            var notAuthorized = invoice.TaxDocumentKind == TaxDocumentKind.Nfe && invoice.NfeStatus != NfeStatus.Authorized;
            (notAuthorized ? unauthorized : included).Add(SaleLine(item, invoice));
        }

        foreach (var item in await purchases.ToListAsync())
        {
            var invoice = item.PurchaseInvoice!;
            var notAuthorized = invoice.IssuerType == DocumentIssuerType.Own
                && invoice.TaxDocumentKind == TaxDocumentKind.Nfe
                && invoice.NfeStatus != NfeStatus.Authorized;
            (notAuthorized ? unauthorized : included).Add(PurchaseLine(item, invoice));
        }

        return (Sort(included), Sort(unauthorized));
    }

    private static List<FiscalDocumentLineDto> Sort(List<FiscalDocumentLineDto> lines) => lines
        .OrderBy(l => l.Date)
        .ThenBy(l => l.DocumentType, StringComparer.Ordinal)
        .ThenBy(l => l.Number, StringComparer.Ordinal)
        .ThenBy(l => l.ItemCode, StringComparer.Ordinal)
        .ToList();

    private static FiscalDocumentLineDto SaleLine(SalesInvoiceItem item, SalesInvoice invoice) => new()
    {
        Direction = DirectionOf(item.Cfop, "S"),
        Date = invoice.InvoiceDate ?? default,
        Model = ModelOf(invoice.TaxDocumentKind),
        Series = invoice.TaxDocumentSeries ?? "",
        Number = invoice.TaxDocumentNumber ?? invoice.InvoiceNumber ?? "",
        InternalNumber = invoice.InvoiceNumber ?? "",
        AccessKey = invoice.ChaveNFe,
        Partner = Partner(invoice.CardCode, invoice.CardName),
        ItemCode = item.ItemCode,
        ItemName = item.ItemName ?? "",
        Cfop = item.Cfop ?? "",
        CstIcms = item.CstIcms ?? "",
        OperationValue = item.GrandTotal,
        IcmsBase = item.IcmsBase,
        IcmsRate = item.IcmsRate,
        IcmsValue = item.IcmsValue,
        CstPis = item.CstPis ?? "",
        PisValue = item.PisValue,
        CstCofins = item.CstCofins ?? "",
        CofinsValue = item.CofinsValue,
        DocumentType = "Saída",
        DocumentKey = invoice.Key,
    };

    private static FiscalDocumentLineDto PurchaseLine(PurchaseInvoiceItem item, PurchaseInvoice invoice) => new()
    {
        Direction = DirectionOf(item.Cfop, "E"),
        Date = invoice.PostingDate ?? invoice.IssueDate ?? default,
        Model = ModelOf(invoice.TaxDocumentKind),
        Series = invoice.TaxDocumentSeries ?? "",
        Number = invoice.TaxDocumentNumber ?? invoice.InvoiceNumber ?? "",
        InternalNumber = invoice.InvoiceNumber ?? "",
        AccessKey = invoice.ChaveNFe,
        Partner = Partner(invoice.CardCode, invoice.CardName),
        ItemCode = item.ItemCode ?? "",
        ItemName = item.ItemName ?? "",
        Cfop = item.Cfop ?? "",
        CstIcms = item.CstIcms ?? "",
        OperationValue = item.GrandTotal,
        IcmsBase = item.IcmsBase,
        IcmsRate = item.IcmsRate,
        IcmsValue = item.IcmsValue,
        CstPis = item.CstPis ?? "",
        PisValue = item.PisValue,
        CstCofins = item.CstCofins ?? "",
        CofinsValue = item.CofinsValue,
        DocumentType = "Entrada",
        DocumentKey = invoice.Key,
    };

    /// <summary>1/2/3 = entrada, 5/6/7 = saída; sem CFOP vale a direção do documento.</summary>
    private static string DirectionOf(string? cfop, string documentDirection) => cfop?.Trim() switch
    {
        [var first, ..] when first is '1' or '2' or '3' => "E",
        [var first, ..] when first is '5' or '6' or '7' => "S",
        _ => documentDirection,
    };

    private static string ModelOf(TaxDocumentKind kind) => kind == TaxDocumentKind.Nfe ? "55" : "01";

    private static string Partner(string code, string? name) => string.IsNullOrWhiteSpace(name) ? code : $"({code}) {name}";
}
