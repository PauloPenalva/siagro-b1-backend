using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Helpers;

namespace SiagroB1.Reports.Services;

/// <summary>
/// "Remessas para Depósito": uma linha por remessa ativa do período, com a posição ATUAL de Remetido, Retornado,
/// A retornar e Expedido sem retorno — os mesmos números da seção da liberação
/// (<c>ShipmentReleasesStorageRemittanceSummaryService</c>). Agrupado pelo produto + UM da primeira linha da
/// remessa. Só STANDALONE (a guarda fica no controller).
/// </summary>
public class StorageRemittancesByPeriodReportService(IUnitOfWork db, IFastReportService reportService)
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private sealed record ReturnDocument(string Number, DateTime? IssueDate, InvoiceStatus Status, decimal Quantity);

    private sealed record RemittancePosition(
        SalesInvoice Remittance,
        ShipmentRelease? Release,
        string Group,
        string Number,
        string UnitOfMeasure,
        List<ReturnDocument> Returns,
        List<StorageRemittanceData.TransactionLine> Shipped,
        decimal Remitted,
        decimal Returned,
        decimal ShippedWithoutReturn)
    {
        public decimal PendingReturn => Remitted - Returned;
    }

    public async Task<List<StorageRemittanceRowDto>> BuildRowsAsync(StorageRemittancesByPeriodRequest request)
    {
        var context = db.Context;
        var query = StorageRemittanceData.RemittancesQuery(context, request);

        var remittances = await query.AsNoTracking().Include(i => i.Items).ToListAsync();
        if (remittances.Count == 0)
            return [];

        var releaseKeys = StorageRemittanceData.ReleaseKeys(query);
        var releases = await context.ShipmentReleases.AsNoTracking()
            .Include(r => r.PurchaseContract)
            .Where(r => releaseKeys.Contains(r.Key))
            .ToDictionaryAsync(r => r.Key);
        var returnLines = await StorageRemittanceData
            .ReturnLinesQuery(context, StorageRemittanceData.RemittanceItemKeys(query))
            .ToListAsync();
        var transactions = await StorageRemittanceData.TransactionsQuery(context, releaseKeys).ToListAsync();

        var groupOf = InvoiceItemGrouping.BuildGroupResolver(
            remittances, i => FirstLine(i)?.ItemCode, i => FirstLine(i)?.ItemName, i => FirstLine(i)?.UnitOfMeasureCode);

        var positions = remittances
            .Select(i => Position(i, releases.GetValueOrDefault(i.ShipmentReleaseKey!.Value), groupOf(i), returnLines, transactions))
            .Where(p => Matches(p, request.ReturnSituation))
            .OrderBy(p => p.Group, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(p => p.Remittance.InvoiceDate)
            .ThenBy(p => p.Number, StringComparer.Ordinal)
            .ThenBy(p => p.Remittance.Key);

        var rows = new List<StorageRemittanceRowDto>();
        foreach (var position in positions)
        {
            rows.Add(RemittanceRow(position));
            if (!request.Detailed)
                continue;

            rows.AddRange(position.Returns.Select(r => ReturnRow(position, r)));
            rows.AddRange(position.Shipped.Select(t => TransactionRow(position, t)));
        }

        return rows;
    }

    private static RemittancePosition Position(
        SalesInvoice remittance,
        ShipmentRelease? release,
        string group,
        List<StorageRemittanceData.ReturnLine> returnLines,
        List<StorageRemittanceData.TransactionLine> transactions)
    {
        var itemKeys = remittance.Items.Where(l => l.Key != null).Select(l => l.Key!.Value).ToHashSet();

        var returns = returnLines
            .Where(l => itemKeys.Contains(l.RemittanceItemKey))
            .GroupBy(l => l.PurchaseInvoiceKey)
            .Select(g => new ReturnDocument(
                g.Max(l => l.Number) ?? "", g.Max(l => l.IssueDate), g.Max(l => l.Status), g.Sum(l => l.Quantity)))
            .OrderBy(r => r.IssueDate)
            .ThenBy(r => r.Number, StringComparer.Ordinal)
            .ToList();

        var origin = release?.Origin ?? ReleaseOrigin.Standard;
        var shipped = transactions
            .Where(t => t.ReleaseKey == remittance.ShipmentReleaseKey && StorageRemittanceData.IsShipped(t, origin))
            .OrderBy(t => t.TransactionDate)
            .ThenBy(t => t.Code, StringComparer.Ordinal)
            .ToList();

        return new RemittancePosition(
            remittance,
            release,
            group,
            DocumentNumber(remittance.TaxDocumentNumber, remittance.InvoiceNumber),
            FirstLine(remittance)?.UnitOfMeasureCode ?? "",
            returns,
            shipped,
            remittance.Items.Sum(l => l.Quantity),
            returns.Sum(r => r.Quantity),
            shipped.Where(t => !t.HasReturn).Sum(t => t.NetWeight));
    }

    private static bool Matches(RemittancePosition position, StorageRemittanceSituation situation) => situation switch
    {
        StorageRemittanceSituation.Open => position.PendingReturn > 0,
        StorageRemittanceSituation.Returned => position.PendingReturn <= 0,
        _ => true,
    };

    public async Task<byte[]> ExecuteAsync(StorageRemittancesByPeriodRequest request)
    {
        var rows = await BuildRowsAsync(request);
        var first = rows.FirstOrDefault(r => r.Kind == StorageRemittanceRowKind.Remittance);

        var extra = new List<string>();
        if (!string.IsNullOrWhiteSpace(request.CardCode))
            extra.Add($"Armazém: {ReportText.Describe(first?.Warehouse, request.CardCode)}");
        if (request.Detailed)
            extra.Add("Detalhado");

        var parameters = new Dictionary<string, object>
        {
            ["pFilters"] = LogisticsReportText.BuildFilters(
                "Data da remessa",
                request,
                await LogisticsReportText.BranchNameAsync(db.Context, request.BranchCode),
                ReportText.ProductOfGroup(first?.Group),
                SituationText(request.ReturnSituation),
                extra),
            [FastReportService.SingleUomParameter] = ReportText.IsSingleUnit(rows.Select(r => r.UnitOfMeasure)),
        };

        return await reportService.GeneratePdfAsync(
            "StorageRemittancesByPeriod.frx", rows, "StorageRemittances", "StorageRemittances", parameters);
    }

    private static string SituationText(StorageRemittanceSituation situation) => situation switch
    {
        StorageRemittanceSituation.Open => "Situação: em aberto",
        StorageRemittanceSituation.Returned => "Situação: totalmente retornada",
        _ => "Situação: todas",
    };

    private static StorageRemittanceRowDto RemittanceRow(RemittancePosition p) => new()
    {
        Group = p.Group,
        Kind = StorageRemittanceRowKind.Remittance,
        Remittance = p.Number,
        InvoiceDate = ReportText.Date(p.Remittance.InvoiceDate),
        Status = InvoiceReportText.Status(p.Remittance.InvoiceStatus),
        NfeStatus = InvoiceReportText.Nfe(p.Remittance.NfeStatus),
        Warehouse = ReportText.Partner(p.Remittance.CardCode, p.Remittance.CardName),
        Contract = p.Release?.PurchaseContract?.Code ?? "",
        ReleaseDate = ReportText.Date(p.Release?.ReleaseDate),
        UnitOfMeasure = p.UnitOfMeasure,
        RemittedQuantity = p.Remitted,
        ReturnedQuantity = p.Returned,
        PendingReturnQuantity = p.PendingReturn,
        ShippedWithoutReturnQuantity = p.ShippedWithoutReturn,
        RemittedText = Quantity(p.Remitted),
        ReturnedText = Quantity(p.Returned),
        PendingReturnText = Quantity(p.PendingReturn),
        ShippedWithoutReturnText = Quantity(p.ShippedWithoutReturn),
        RemittanceCount = 1,
    };

    private static StorageRemittanceRowDto ReturnRow(RemittancePosition p, ReturnDocument r) => new()
    {
        Group = p.Group,
        Kind = StorageRemittanceRowKind.Return,
        UnitOfMeasure = p.UnitOfMeasure,
        Detail = Join(
            Head("Retorno", r.Number, r.IssueDate),
            InvoiceReportText.Status(r.Status),
            $"{Quantity(r.Quantity)} {p.UnitOfMeasure}".Trim()),
    };

    private static StorageRemittanceRowDto TransactionRow(RemittancePosition p, StorageRemittanceData.TransactionLine t) => new()
    {
        Group = p.Group,
        Kind = StorageRemittanceRowKind.Transaction,
        UnitOfMeasure = p.UnitOfMeasure,
        Detail = Join(
            Head("Romaneio", t.Code, t.TransactionDate),
            string.IsNullOrWhiteSpace(t.TruckCode) ? "" : $"placa {t.TruckCode.Trim()}",
            $"{Quantity(t.NetWeight)} {p.UnitOfMeasure}".Trim(),
            t.HasReturn ? $"retorno {t.ReturnNumber}" : "sem retorno"),
    };

    /// <summary>"Rótulo número de dd/MM/yyyy" — sem data, sem o "de".</summary>
    private static string Head(string label, string? number, DateTime? date)
    {
        var head = $"{label} {number}".Trim();
        return date is null ? head : $"{head} de {ReportText.Date(date)}";
    }

    private static string Join(params string[] parts) =>
        string.Join(" - ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));

    /// <summary>"Primeira" linha: o item não tem número de linha, então ItemCode e depois Key (determinístico).</summary>
    private static SalesInvoiceItem? FirstLine(SalesInvoice remittance) =>
        remittance.Items.OrderBy(l => l.ItemCode, StringComparer.Ordinal).ThenBy(l => l.Key).FirstOrDefault();

    /// <summary>Mesma regra de <c>StorageDepositRules.DocumentNumber</c>.</summary>
    private static string DocumentNumber(string? taxDocumentNumber, string? invoiceNumber) =>
        string.IsNullOrWhiteSpace(taxDocumentNumber) ? invoiceNumber ?? "" : taxDocumentNumber;

    private static string Quantity(decimal value) => value.ToString("N3", PtBr);
}
