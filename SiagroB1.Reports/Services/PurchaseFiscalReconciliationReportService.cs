using System.Globalization;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Infra.Queries;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Helpers;
using R = SiagroB1.Infra.Queries.PurchaseFiscalReconciliationRules;

namespace SiagroB1.Reports.Services;

/// <summary>
/// "Entregas × NF do Fornecedor" (spec 2026-10-13 §8). Lê pela <see cref="PurchaseFiscalReconciliationQuery"/>, a mesma
/// consulta da tela — os números batem por construção. Agrupado por contrato; romaneio rateado aparece em cada contrato
/// e conta uma vez no total geral; NFs com quantidade não vinculada no fim.
/// </summary>
public class PurchaseFiscalReconciliationReportService(IUnitOfWork db, IFastReportService reportService)
{
    public const string UnlinkedGroup = "NFs do fornecedor com quantidade não vinculada";
    public const string NoContractGroup = "Sem contrato";
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public async Task<List<PurchaseFiscalReconciliationRowDto>> BuildRowsAsync(PurchaseFiscalReconciliationReportRequest request)
    {
        var filter = new FiscalReconciliationFilter
        {
            CardCode = request.CardCode ?? "", ItemCode = request.ItemCode ?? "", ContractCode = request.ContractCode,
            DateFrom = request.FromDate, DateTo = request.ToDate,
        };

        var transactions = await PurchaseFiscalReconciliationQuery.LoadTransactionsAsync(db.Context, filter);
        if (request.Statuses.Count > 0)
            transactions = transactions.Where(t => request.Statuses.Contains((int)t.Status)).ToList();

        var rows = new List<PurchaseFiscalReconciliationRowDto>();
        var counted = new HashSet<Guid>();

        var placed = transactions
            .SelectMany(t => (t.ContractCodes.Count == 0 ? [NoContractGroup] : t.ContractCodes.Select(c => $"Contrato {c}"))
                .Select(group => (Group: group, Tx: t)))
            .OrderBy(p => p.Group == NoContractGroup).ThenBy(p => p.Group, StringComparer.Ordinal)
            .ThenBy(p => p.Tx.TransactionDate).ThenBy(p => p.Tx.Code, StringComparer.Ordinal);

        foreach (var (group, tx) in placed)
        {
            var first = counted.Add(tx.Key);
            rows.Add(new PurchaseFiscalReconciliationRowDto
            {
                Group = group, Kind = PurchaseFiscalReconciliationRowKind.Transaction, Document = tx.Code ?? "",
                Date = ReportText.Date(tx.TransactionDate), Description = tx.TransactionTypeName, Party = tx.TruckCode ?? "",
                PhysicalText = Quantity(tx.PhysicalQuantity), FiscalText = Quantity(tx.FiscalQuantity),
                DifferenceText = Quantity(tx.Difference), Status = tx.StatusName,
                GroupPhysical = tx.PhysicalQuantity, GroupFiscal = tx.FiscalQuantity, GroupDifference = tx.Difference,
                SumPhysical = first ? tx.PhysicalQuantity : 0m, SumFiscal = first ? tx.FiscalQuantity : 0m,
                SumDifference = first ? tx.Difference : 0m, TransactionCount = first ? 1 : 0,
            });

            rows.AddRange(tx.Links.Select(link => new PurchaseFiscalReconciliationRowDto
            {
                Group = group, Kind = PurchaseFiscalReconciliationRowKind.Invoice, Document = link.TaxDocument,
                Date = ReportText.Date(link.IssueDate), Description = "NF do fornecedor",
                Party = ReportText.Partner(link.CardCode, link.CardName), FiscalText = Quantity(link.Quantity),
            }));
        }

        filter.IncludeFullyLinked = false;
        var unlinked = await PurchaseFiscalReconciliationQuery.LoadInvoiceLinesAsync(db.Context, filter);
        rows.AddRange(unlinked.Select(line => new PurchaseFiscalReconciliationRowDto
        {
            Group = UnlinkedGroup, Kind = PurchaseFiscalReconciliationRowKind.Unlinked, Document = line.TaxDocument,
            Date = ReportText.Date(line.IssueDate), Description = line.UsageName ?? "",
            Party = ReportText.Partner(line.CardCode, line.CardName),
            FiscalText = line.QuantityKg is { } kg ? Quantity(kg) : $"{line.Quantity.ToString("N3", PtBr)} {line.UnitOfMeasureCode}",
            Status = line.AvailableQuantity is { } available ? $"Livre: {Quantity(available)}" : "UM sem fator",
        }));

        return rows;
    }

    public async Task<byte[]> ExecuteAsync(PurchaseFiscalReconciliationReportRequest request)
    {
        var rows = await BuildRowsAsync(request);
        var filters = new List<string> { $"Fornecedor: {request.CardCode}", $"Produto: {request.ItemCode}" };
        if (!string.IsNullOrWhiteSpace(request.ContractCode)) filters.Add($"Contrato: {request.ContractCode}");
        if (request.FromDate != null || request.ToDate != null)
            filters.Add($"Período: {ReportText.Date(request.FromDate)} a {ReportText.Date(request.ToDate)}");
        if (request.Statuses.Count > 0)
            filters.Add("Situação: " + string.Join(", ", request.Statuses.Select(s => R.StatusName((FiscalReconciliationStatus)s))));

        var parameters = new Dictionary<string, object> { ["pFilters"] = string.Join("  |  ", filters) };

        return await reportService.GeneratePdfAsync(
            "PurchaseFiscalReconciliation.frx", rows, "FiscalReconciliation", "FiscalReconciliation", parameters);
    }

    private static string Quantity(decimal value) => value.ToString("N3", PtBr);
}
