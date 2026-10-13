using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Infra.Queries;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Helpers;

namespace SiagroB1.Reports.Services;

/// <summary>
/// "Custódia de Armazenagem" (spec 2026-10-12 §6): romaneios de lote de terceiro do período com as etapas fiscais
/// esperadas e a nota de cada uma, agrupados por depositante e lote, com o saldo físico e o fiscal em depósito do lote
/// na data final. Mesmas regras da seção do romaneio (StorageCustodyQuery + StorageCustodyRules). Só STANDALONE (a
/// guarda fica no controller).
/// </summary>
public class StorageCustodyByPeriodReportService(IUnitOfWork db, IFastReportService reportService)
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public async Task<List<StorageCustodyRowDto>> BuildRowsAsync(StorageCustodyByPeriodRequest request)
    {
        var ctx = db.Context;
        var from = request.FromDate.Date;
        var toExclusive = request.ToDate.Date.AddDays(1);

        var lots = StorageCustodyQuery.Transactions(ctx);
        if (!string.IsNullOrWhiteSpace(request.BranchCode))
            lots = lots.Where(t => t.BranchCode == request.BranchCode);
        if (!string.IsNullOrWhiteSpace(request.CardCode))
            lots = lots.Where(t => t.CardCode == request.CardCode);
        if (!string.IsNullOrWhiteSpace(request.ItemCode))
            lots = lots.Where(t => t.ItemCode == request.ItemCode);
        if (!string.IsNullOrWhiteSpace(request.StorageAddressCode))
            lots = lots.Where(t => t.StorageAddressCode == request.StorageAddressCode);

        // Todos os romaneios dos lotes filtrados até a data final: o período escolhe as linhas, o saldo fiscal soma tudo.
        var untilEnd = await lots.Where(t => t.TransactionDate < toExclusive).ToListAsync();
        var inPeriod = untilEnd.Where(t => t.TransactionDate >= from).ToList();
        if (inPeriod.Count == 0)
            return [];

        var documents = await StorageCustodyQuery.ActiveDocumentsAsync(ctx, untilEnd.Select(t => t.Key).ToList());
        var lotCodes = inPeriod.Select(t => t.StorageAddressCode).Distinct().ToList();
        var physical = await PhysicalBalancesAsync(lotCodes, toExclusive);
        var fiscal = FiscalBalances(untilEnd, documents);
        var balanceLabel = $"Saldo do lote em {ReportText.Date(request.ToDate)} — físico, fiscal em depósito e diferença:";

        var rows = new List<StorageCustodyRowDto>();
        foreach (var t in inPeriod
                     .OrderBy(t => GroupOf(t), StringComparer.CurrentCultureIgnoreCase)
                     .ThenBy(t => t.TransactionDate)
                     .ThenBy(t => t.Code, StringComparer.Ordinal))
        {
            var steps = StorageCustodyRules.BuildSteps(t, documents);
            if (steps.Count == 0)
                continue;

            var pending = steps.Any(s => s.Situation == StorageCustodyStepSituation.Missing);
            if (request.Situation == StorageCustodySituationFilter.Pending && !pending) continue;
            if (request.Situation == StorageCustodySituationFilter.Complete && pending) continue;

            var lotPhysical = physical.GetValueOrDefault(t.StorageAddressCode);
            var lotFiscal = fiscal.GetValueOrDefault(t.StorageAddressCode);

            StorageCustodyRowDto Base() => new()
            {
                Group = GroupOf(t),
                LotBalanceLabel = balanceLabel,
                LotPhysicalBalance = lotPhysical,
                LotFiscalBalance = lotFiscal,
                LotDifference = lotPhysical - lotFiscal,
            };

            var romaneio = Base();
            romaneio.Kind = StorageCustodyRowKind.Romaneio;
            romaneio.Transaction = t.Code ?? "";
            romaneio.Description = TypeText(t);
            romaneio.Date = ReportText.Date(t.TransactionDate);
            romaneio.GrossWeightText = Quantity(t.GrossWeight);
            romaneio.NetWeightText = Quantity(t.NetWeight);
            romaneio.RomaneioCount = 1;
            romaneio.PendingCount = pending ? 1 : 0;
            rows.Add(romaneio);

            foreach (var s in steps)
            {
                var step = Base();
                step.Kind = StorageCustodyRowKind.Step;
                step.Description = "· " + StorageCustodyRules.ShortRoleText(s.Role);
                step.Date = ReportText.Date(s.IssueDate);
                step.Cfop = s.Cfop ?? "";
                step.Document = s.InvoiceNumber ?? "";
                step.TaxDocument = TaxDocument(s.Number, s.TaxDocumentSeries, s.InvoiceNumber);
                step.Situation = s.SituationText;
                step.NfeStatus = s.NfeStatus is { } nfe ? InvoiceReportText.Nfe(nfe) : "";
                step.QuantityText = s.Quantity is { } q ? Quantity(q) : "";
                step.DifferenceText = s.Difference is { } d ? Quantity(d) : "";
                rows.Add(step);
            }
        }

        return rows;
    }

    public async Task<byte[]> ExecuteAsync(StorageCustodyByPeriodRequest request)
    {
        var rows = await BuildRowsAsync(request);

        var extra = new List<string>();
        if (!string.IsNullOrWhiteSpace(request.CardCode))
            extra.Add($"Depositante: {request.CardCode}");
        if (!string.IsNullOrWhiteSpace(request.StorageAddressCode))
            extra.Add($"Lote: {request.StorageAddressCode}");

        var parameters = new Dictionary<string, object>
        {
            ["pFilters"] = LogisticsReportText.BuildFilters(
                "Data do romaneio", request, await LogisticsReportText.BranchNameAsync(db.Context, request.BranchCode),
                productName: null, SituationText(request.Situation), extra),
        };

        return await reportService.GeneratePdfAsync(
            "StorageCustodyByPeriod.frx", rows, "StorageCustody", "StorageCustody", parameters);
    }

    /// <summary>Saldo físico do lote na data final: mesma fórmula de StorageAddress.Balance (NetWeight).</summary>
    private async Task<Dictionary<string, decimal>> PhysicalBalancesAsync(List<string> lotCodes, DateTime toExclusive)
    {
        var sums = await db.Context.StorageTransactions.AsNoTracking()
            .Where(t => t.StorageAddressCode != null && lotCodes.Contains(t.StorageAddressCode)
                        && t.TransactionDate < toExclusive
                        && (t.TransactionStatus == StorageTransactionsStatus.Confirmed || t.TransactionStatus == StorageTransactionsStatus.Invoiced))
            .GroupBy(t => new { t.StorageAddressCode, t.TransactionType })
            .Select(g => new { g.Key.StorageAddressCode, g.Key.TransactionType, Net = g.Sum(t => t.NetWeight) })
            .ToListAsync();

        return sums
            .GroupBy(s => s.StorageAddressCode!)
            .ToDictionary(g => g.Key, g => g.Sum(s => Sign(s.TransactionType) * s.Net));
    }

    private static decimal Sign(StorageTransactionType type) => type switch
    {
        StorageTransactionType.Receipt or StorageTransactionType.ShipmentReleased or StorageTransactionType.SalesShipmentReturn => 1m,
        StorageTransactionType.Shipment or StorageTransactionType.SalesShipment or StorageTransactionType.TechnicalLoss => -1m,
        _ => 0m,
    };

    /// <summary>Saldo fiscal em depósito (spec §3.5): entradas para armazenagem − retornos, só notas ativas.</summary>
    private static Dictionary<string, decimal> FiscalBalances(List<StorageCustodyTransactionRow> transactions, List<StorageCustodyDocumentRow> documents)
    {
        var lotOf = transactions.ToDictionary(t => t.Key, t => t.StorageAddressCode);

        return documents
            .Where(d => lotOf.ContainsKey(d.TransactionKey))
            .GroupBy(d => lotOf[d.TransactionKey])
            .ToDictionary(g => g.Key, g => g.Sum(d => d.Role switch
            {
                StorageCustodyRole.StorageEntry => d.Quantity,
                StorageCustodyRole.StorageReturn or StorageCustodyRole.StorageSymbolicReturn => -d.Quantity,
                _ => 0m,
            }));
    }

    private static string GroupOf(StorageCustodyTransactionRow t) =>
        $"{ReportText.Partner(t.CardCode, t.CardName)} — Lote {t.StorageAddressCode} — {t.ItemName ?? t.ItemCode} ({t.UoM})";

    private static string TypeText(StorageCustodyTransactionRow t) =>
        (t.TransactionType == StorageTransactionType.Receipt ? "Recepção" : "Expedição") + (t.IsOwnershipTransfer ? " (transferência)" : "");

    private static string SituationText(StorageCustodySituationFilter situation) => situation switch
    {
        StorageCustodySituationFilter.Pending => "Situação: com pendência fiscal",
        StorageCustodySituationFilter.Complete => "Situação: completa",
        _ => "Situação: todas",
    };

    /// <summary>"número-série" quando há NF; senão vazio (o número interno já sai na coluna Documento).</summary>
    private static string TaxDocument(string? number, string? series, string? internalNumber)
    {
        if (string.IsNullOrWhiteSpace(number) || number == internalNumber)
            return "";
        return string.IsNullOrWhiteSpace(series) ? number.Trim() : $"{number.Trim()}-{series.Trim()}";
    }

    private static string Quantity(decimal value) => value.ToString("N3", PtBr);
}
