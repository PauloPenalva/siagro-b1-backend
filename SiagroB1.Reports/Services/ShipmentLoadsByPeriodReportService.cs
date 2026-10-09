using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Helpers;

namespace SiagroB1.Reports.Services;

/// <summary>
/// Cargas por período, agrupadas por produto + UM. Tudo sai de SHIPMENT_LOADS (snapshots de
/// produto, armazém, transportadora) e das notas de saída da carga (cliente); nada de
/// ITEMS/BUSINESS_PARTNERS/WAREHOUSES, vazias em SAPB1. O saldo é o do domínio
/// (<see cref="ShipmentLoad.AvailableQuantity"/>): zero quando cancelada.
/// </summary>
public class ShipmentLoadsByPeriodReportService(IUnitOfWork db, IFastReportService reportService)
{
    public async Task<List<ShipmentLoadRowDto>> BuildRowsAsync(ShipmentLoadsByPeriodRequest request) =>
        ToRows(await LoadAsync(request));

    public async Task<byte[]> ExecuteAsync(ShipmentLoadsByPeriodRequest request)
    {
        var loads = await LoadAsync(request);
        var rows = ToRows(loads);
        var first = rows.Count > 0 ? rows[0] : null;
        var statuses = LogisticsReportText.EffectiveLoadStatuses(request.Statuses);

        var extra = new List<string>();
        if (request.LoadType is { } type)
            extra.Add($"Tipo: {LogisticsReportText.LoadTypeText(type)}");
        if (!string.IsNullOrWhiteSpace(request.WarehouseCode))
            extra.Add($"Armazém: {ReportText.Describe(first?.Warehouse, request.WarehouseCode)}");
        if (!string.IsNullOrWhiteSpace(request.CarrierCardCode))
            extra.Add($"Transportadora: {ReportText.Describe(first?.Carrier, request.CarrierCardCode)}");
        if (!string.IsNullOrWhiteSpace(request.TruckCode))
            extra.Add($"Placa: {request.TruckCode}");
        if (!string.IsNullOrWhiteSpace(request.CardCode))
            extra.Add($"Cliente: {ReportText.Describe(LogisticsReportText.CustomerName(loads, request.CardCode), request.CardCode)}");

        var parameters = new Dictionary<string, object>
        {
            ["pFilters"] = LogisticsReportText.BuildFilters(
                "Data da carga",
                request,
                await LogisticsReportText.BranchNameAsync(db.Context, request.BranchCode),
                ReportText.ProductOfGroup(first?.Group),
                LogisticsReportText.StatusFilter(statuses, LogisticsReportText.LoadStatusText, ShipmentLoadStatus.Cancelled),
                extra),
            [FastReportService.SingleUomParameter] = ReportText.IsSingleUnit(rows.Select(r => r.UnitOfMeasure)),
        };

        return await reportService.GeneratePdfAsync(
            "ShipmentLoadsByPeriod.frx", rows, "ShipmentLoads", "ShipmentLoads", parameters);
    }

    private async Task<List<ShipmentLoad>> LoadAsync(ShipmentLoadsByPeriodRequest request)
    {
        var from = request.FromDate.Date;
        var toExclusive = request.ToDate.Date.AddDays(1);
        var statuses = LogisticsReportText.EffectiveLoadStatuses(request.Statuses);

        // As notas vêm por Include (e não por um segundo SELECT com IN de chaves): o cliente da
        // carga é o snapshot CardCode/CardName das notas vivas.
        var query = db.Context.ShipmentLoads
            .AsNoTracking()
            .Include(l => l.Invoices)
            .Where(l => l.LoadDate >= from && l.LoadDate < toExclusive)
            .Where(l => statuses.Contains(l.Status));

        if (!string.IsNullOrWhiteSpace(request.BranchCode))
            query = query.Where(l => l.BranchCode == request.BranchCode);

        if (!string.IsNullOrWhiteSpace(request.ItemCode))
            query = query.Where(l => l.ItemCode == request.ItemCode);

        if (request.LoadType is { } type)
            query = query.Where(l => l.LoadType == type);

        if (!string.IsNullOrWhiteSpace(request.WarehouseCode))
            query = query.Where(l => l.WarehouseCode == request.WarehouseCode);

        if (!string.IsNullOrWhiteSpace(request.CarrierCardCode))
            query = query.Where(l => l.CarrierCardCode == request.CarrierCardCode);

        if (!string.IsNullOrWhiteSpace(request.TruckCode))
            query = query.Where(l => l.TruckCode == request.TruckCode);

        var loads = await query.ToListAsync();

        // Cliente depende de regra (nota viva, senão o planejado): filtrado em C#.
        if (request.CardCode is { } cardCode && !string.IsNullOrWhiteSpace(cardCode))
            loads = loads.Where(l => LogisticsReportText.LoadHasCustomer(l, cardCode)).ToList();

        return loads;
    }

    private static List<ShipmentLoadRowDto> ToRows(List<ShipmentLoad> loads)
    {
        var groupOf = InvoiceItemGrouping.BuildGroupResolver(
            loads, l => l.ItemCode, l => l.ItemName, l => l.UnitOfMeasureCode);

        return loads
            .OrderBy(l => groupOf(l), StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(l => l.LoadDate)
            .ThenBy(l => l.Code ?? "", StringComparer.Ordinal)
            .Select(l => ToRow(l, groupOf(l)))
            .ToList();
    }

    private static ShipmentLoadRowDto ToRow(ShipmentLoad l, string group) => new()
    {
        Group = group,
        Code = l.Code ?? "",
        LoadDate = ReportText.Date(l.LoadDate),
        Type = LogisticsReportText.LoadTypeText(l.LoadType),
        Status = LogisticsReportText.LoadStatusText(l.Status),
        Truck = l.TruckCode ?? "",
        Carrier = ReportText.NameOrCode(l.CarrierCardCode, l.CarrierName),
        Warehouse = ReportText.NameOrCode(l.WarehouseCode, l.WarehouseName),
        Customers = LogisticsReportText.LoadCustomers(l),
        UnitOfMeasure = l.UnitOfMeasureCode,
        TotalQuantity = l.TotalQuantity,
        InvoicedQuantity = l.InvoicedQuantity,
        ReturnedQuantity = l.ReturnedToWarehouseQuantity,
        TransshippedQuantity = l.TransshippedQuantity,
        DischargedQuantity = l.DischargedQuantity,
        BalanceQuantity = l.AvailableQuantity,
        FreightValue = l.FreightPrice ?? 0m,
    };
}
