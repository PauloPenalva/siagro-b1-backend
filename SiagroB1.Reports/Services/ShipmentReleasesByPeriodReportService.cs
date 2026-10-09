using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Helpers;

namespace SiagroB1.Reports.Services;

/// <summary>
/// Liberações de embarque de contratos de COMPRA, agrupadas pelo produto + UM do contrato.
/// Fornecedor e produto são snapshots de PURCHASE_CONTRACTS (tabela local); o armazém de retirada
/// é o snapshot DeliveryLocationCode/Name da liberação — nada de WAREHOUSES. Retirado =
/// ShippedQuantity; saldo = <see cref="ShipmentRelease.AvailableQuantity"/>.
/// </summary>
public class ShipmentReleasesByPeriodReportService(IUnitOfWork db, IFastReportService reportService)
{
    public async Task<List<ShipmentReleaseRowDto>> BuildRowsAsync(ShipmentReleasesByPeriodRequest request)
    {
        var from = request.FromDate.Date;
        var toExclusive = request.ToDate.Date.AddDays(1);
        var statuses = LogisticsReportText.EffectiveReleaseStatuses(request.Statuses);

        var query = db.Context.ShipmentReleases
            .AsNoTracking()
            .Include(r => r.PurchaseContract)
            .Where(r => r.ReleaseDate >= from && r.ReleaseDate < toExclusive)
            .Where(r => statuses.Contains(r.Status));

        if (!string.IsNullOrWhiteSpace(request.BranchCode))
            query = query.Where(r => r.BranchCode == request.BranchCode);

        if (!string.IsNullOrWhiteSpace(request.ItemCode))
            query = query.Where(r => r.PurchaseContract!.ItemCode == request.ItemCode);

        if (!string.IsNullOrWhiteSpace(request.ContractCode))
            query = query.Where(r => r.PurchaseContract!.Code == request.ContractCode);

        if (!string.IsNullOrWhiteSpace(request.CardCode))
            query = query.Where(r => r.PurchaseContract!.CardCode == request.CardCode);

        if (!string.IsNullOrWhiteSpace(request.DeliveryLocationCode))
            query = query.Where(r => r.DeliveryLocationCode == request.DeliveryLocationCode);

        if (request.Origin is { } origin)
            query = query.Where(r => r.Origin == origin);

        var releases = (await query.ToListAsync()).Where(r => r.PurchaseContract is not null).ToList();

        var groupOf = InvoiceItemGrouping.BuildGroupResolver(
            releases, r => r.PurchaseContract!.ItemCode, r => r.PurchaseContract!.ItemName, r => r.PurchaseContract!.UnitOfMeasureCode);

        return releases
            .OrderBy(r => groupOf(r), StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(r => r.ReleaseDate)
            .ThenBy(r => r.PurchaseContract!.Code ?? "", StringComparer.Ordinal)
            .ThenBy(r => r.DeliveryLocationCode, StringComparer.Ordinal)
            .ThenBy(r => r.ReleasedQuantity)
            .ThenBy(r => r.Key)
            .Select(r => ToRow(r, groupOf(r)))
            .ToList();
    }

    public async Task<byte[]> ExecuteAsync(ShipmentReleasesByPeriodRequest request)
    {
        var rows = await BuildRowsAsync(request);
        var first = rows.Count > 0 ? rows[0] : null;
        var statuses = LogisticsReportText.EffectiveReleaseStatuses(request.Statuses);

        var extra = new List<string>();
        if (!string.IsNullOrWhiteSpace(request.ContractCode))
            extra.Add($"Contrato: {request.ContractCode}");
        if (!string.IsNullOrWhiteSpace(request.CardCode))
            extra.Add($"Fornecedor: {ReportText.Describe(first?.Supplier, request.CardCode)}");
        if (!string.IsNullOrWhiteSpace(request.DeliveryLocationCode))
            extra.Add($"Armazém de retirada: {ReportText.Describe(first?.DeliveryLocation, request.DeliveryLocationCode)}");
        if (request.Origin is { } origin)
            extra.Add($"Origem: {LogisticsReportText.OriginText(origin)}");

        var parameters = new Dictionary<string, object>
        {
            ["pFilters"] = LogisticsReportText.BuildFilters(
                "Data da liberação",
                request,
                await LogisticsReportText.BranchNameAsync(db.Context, request.BranchCode),
                ReportText.ProductOfGroup(first?.Group),
                LogisticsReportText.StatusFilter(statuses, LogisticsReportText.ReleaseStatusText, ReleaseStatus.Cancelled),
                extra),
            [FastReportService.SingleUomParameter] = ReportText.IsSingleUnit(rows.Select(r => r.UnitOfMeasure)),
        };

        return await reportService.GeneratePdfAsync(
            "ShipmentReleasesByPeriod.frx", rows, "ShipmentReleases", "ShipmentReleases", parameters);
    }

    private static ShipmentReleaseRowDto ToRow(ShipmentRelease r, string group)
    {
        var contract = r.PurchaseContract!;

        return new ShipmentReleaseRowDto
        {
            Group = group,
            ReleaseDate = ReportText.Date(r.ReleaseDate),
            Contract = contract.Code ?? "",
            Supplier = ReportText.Partner(contract.CardCode, contract.CardName),
            DeliveryLocation = ReportText.NameOrCode(r.DeliveryLocationCode, r.DeliveryLocationName),
            Origin = LogisticsReportText.OriginText(r.Origin),
            Status = LogisticsReportText.ReleaseStatusText(r.Status),
            UnitOfMeasure = contract.UnitOfMeasureCode,
            ReleasedQuantity = r.ReleasedQuantity,
            WithdrawnQuantity = r.ShippedQuantity,
            BalanceQuantity = r.AvailableQuantity,
        };
    }
}
