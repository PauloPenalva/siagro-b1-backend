using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services.Sped;

namespace SiagroB1.Reports.Services.SpedFiscal;

/// <summary>
/// Lê do banco tudo o que o SPED Fiscal do mês precisa: linhas escrituráveis (as mesmas da
/// Conferência Fiscal), NF-e próprias canceladas/denegadas/inutilizadas, participantes, produtos,
/// unidades, fatores de conversão e, opcionalmente, o inventário.
/// </summary>
public class SpedFiscalDataLoader(IUnitOfWork db, FiscalConferenceService conference, FiscalInventoryService inventory,
    SpedMasterDataLoader masterData)
{
    /// <summary>Só o documento de terceiro tem C170; a unidade da NF-e própria não exige 0220 nem bloqueia o arquivo.</summary>
    private static bool WritesItemLine(FiscalDocumentLineDto line) => line.IssuerType == "1";

    public async Task<SpedFiscalData> LoadAsync(SpedFiscalRequest request)
    {
        var branch = await db.Context.Branchs.AsNoTracking().FirstOrDefaultAsync(b => b.Code == request.BranchCode)
                     ?? throw new InvalidOperationException($"Filial {request.BranchCode} não encontrada.");
        var settings = await db.Context.SpedFiscalSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.BranchCode == request.BranchCode);

        var (lines, _) = await conference.LoadLinesAsync(new FiscalConferenceRequest
        {
            BranchCode = request.BranchCode, Year = request.Year, Month = request.Month,
        });

        FiscalInventoryData? inventoryData = null;
        if (request.IncludeInventory)
            inventoryData = await inventory.BuildAsync(new FiscalInventoryRequest
            {
                BranchCode = request.BranchCode,
                Date = request.InventoryDate ?? new DateTime(request.Year - 1, 12, 31),
            });

        var master = await masterData.LoadAsync(branch, lines, inventoryData?.Rows, WritesItemLine);

        return new SpedFiscalData
        {
            Branch = branch,
            Settings = settings,
            Lines = lines,
            VoidDocuments = await LoadVoidDocumentsAsync(request),
            Participants = master.Participants,
            MissingParticipantCodes = master.MissingParticipantCodes,
            UnconvertibleUnits = master.UnconvertibleUnits,
            Items = master.Items,
            Units = master.Units,
            Conversions = master.Conversions,
            Inventory = inventoryData,
        };
    }

    /// <summary>
    /// NF-e própria do mês com situação cancelada (02), denegada (04) ou inutilizada (05). Não filtra
    /// pela situação do documento: a NF-e cancelada costuma ter o documento cancelado também.
    /// </summary>
    private async Task<List<SpedVoidDocument>> LoadVoidDocumentsAsync(SpedFiscalRequest request)
    {
        var from = new DateTime(request.Year, request.Month, 1);
        var toExclusive = from.AddMonths(1);
        NfeStatus[] statuses = [NfeStatus.Cancelled, NfeStatus.Denied, NfeStatus.Voided];

        var sales = await db.Context.SalesInvoices.AsNoTracking()
            .Where(s => s.BranchCode == request.BranchCode && s.TaxDocumentKind == TaxDocumentKind.Nfe)
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
            .Where(p => p.BranchCode == request.BranchCode && p.TaxDocumentKind == TaxDocumentKind.Nfe)
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

    /// <summary>Produtos e conversões pela regra do SPED Fiscal (só a linha de terceiro é escrita).</summary>
    public async Task<(List<SpedItem> Items, List<SpedUnitConversion> Conversions, List<SpedUnconvertibleUnit> Unconvertible)> LoadItemsAsync(
        Branch branch, List<FiscalDocumentLineDto> lines, FiscalInventoryData? inventoryData)
    {
        var data = await masterData.LoadAsync(branch, lines, inventoryData?.Rows, WritesItemLine);
        return (data.Items, data.Conversions, data.UnconvertibleUnits);
    }
}
