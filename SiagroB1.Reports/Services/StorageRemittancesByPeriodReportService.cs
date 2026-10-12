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
/// remessa. Cada retorno sai sempre logo abaixo da remessa, com o saldo A retornar depois dele; Detalhado acrescenta os
/// romaneios de carregamento. Só STANDALONE (a guarda fica no controller).
/// </summary>
public class StorageRemittancesByPeriodReportService(IUnitOfWork db, IFastReportService reportService)
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private sealed record ReturnDocument(
        Guid Key, string InvoiceNumber, string TaxDocument, string AccessKey, DateTime? IssueDate, InvoiceStatus Status,
        NfeStatus NfeStatus, decimal Quantity);

    private sealed record RemittancePosition(
        SalesInvoice Remittance,
        ShipmentRelease? Release,
        string Group,
        string TaxDocument,
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
            .ThenBy(p => p.TaxDocument, StringComparer.Ordinal)
            .ThenBy(p => p.Remittance.Key);

        var rows = new List<StorageRemittanceRowDto>();
        foreach (var position in positions)
        {
            rows.Add(RemittanceRow(position));

            var pending = position.Remitted;
            foreach (var ret in position.Returns)
            {
                pending -= ret.Quantity;
                rows.Add(ReturnRow(position, ret, pending));
            }

            if (request.Detailed)
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
            .Select(g =>
            {
                var head = g.First();
                return new ReturnDocument(
                    g.Key, head.InvoiceNumber ?? "", TaxDocument(head.TaxDocumentNumber, head.TaxDocumentSeries),
                    AccessKey(head.AccessKey), head.IssueDate, head.Status, head.NfeStatus, g.Sum(l => l.Quantity));
            })
            .OrderBy(r => r.IssueDate)
            .ThenBy(r => r.TaxDocument, StringComparer.Ordinal)
            .ThenBy(r => r.InvoiceNumber, StringComparer.Ordinal)
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
            TaxDocument(remittance.TaxDocumentNumber, remittance.TaxDocumentSeries),
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
        Document = p.Remittance.InvoiceNumber ?? "",
        TaxDocument = p.TaxDocument,
        AccessKey = AccessKey(p.Remittance.ChaveNFe),
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

    /// <summary>
    /// Retorno com as colunas da remessa: quantidade em Retornado, saldo depois dele em A retornar (só texto — os
    /// totais somam a remessa) e, em <see cref="StorageRemittanceRowDto.Note"/>, os romaneios conciliados com ele.
    /// </summary>
    private static StorageRemittanceRowDto ReturnRow(RemittancePosition p, ReturnDocument r, decimal pendingAfter) => new()
    {
        Group = p.Group,
        Kind = StorageRemittanceRowKind.Return,
        Document = r.InvoiceNumber,
        TaxDocument = r.TaxDocument,
        AccessKey = r.AccessKey,
        InvoiceDate = ReportText.Date(r.IssueDate),
        Status = InvoiceReportText.Status(r.Status),
        NfeStatus = InvoiceReportText.Nfe(r.NfeStatus),
        Note = ReconciledNote(p, r),
        UnitOfMeasure = p.UnitOfMeasure,
        ReturnedText = Quantity(r.Quantity),
        PendingReturnText = Quantity(pendingAfter),
    };

    private static string ReconciledNote(RemittancePosition p, ReturnDocument r)
    {
        var loads = p.Shipped
            .Where(t => t.ReturnKey == r.Key)
            .Select(t => $"{t.Code} ({$"{Quantity(t.NetWeight)} {p.UnitOfMeasure}".Trim()})")
            .ToList();
        return loads.Count == 0
            ? "Retorno - sem romaneio conciliado"
            : "Retorno - romaneios: " + string.Join(", ", loads);
    }

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

    /// <summary>"número-série"; sem série, só o número; sem número (remessa sem nota), vazio.</summary>
    private static string TaxDocument(string? number, string? series)
    {
        if (string.IsNullOrWhiteSpace(number))
            return "";
        return string.IsNullOrWhiteSpace(series) ? number.Trim() : $"{number.Trim()}-{series.Trim()}";
    }

    /// <summary>Chave da NF-e em grupos de 4 dígitos, como no DANFE.</summary>
    private static string AccessKey(string? key)
    {
        var digits = key?.Trim() ?? "";
        return string.Join(" ", digits.Chunk(4).Select(c => new string(c)));
    }

    private static string Quantity(decimal value) => value.ToString("N3", PtBr);
}
