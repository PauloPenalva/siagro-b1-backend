using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Helpers;

namespace SiagroB1.Reports.Services;

/// <summary>
/// Romaneios de venda (<see cref="StorageTransactionType.SalesShipment"/>), agrupados por produto
/// + UM. O CardCode/CardName do romaneio de venda é o FORNECEDOR da perna de compra; o cliente
/// chega pela carga (notas vivas; sem nota, o planejado) — romaneio sem carga fica sem cliente.
/// Descontos = secagem + limpeza + outros (o campo chama-se OthersDicount).
/// </summary>
public class SalesShipmentsByPeriodReportService(IUnitOfWork db, IFastReportService reportService)
{
    public async Task<List<SalesShipmentRowDto>> BuildRowsAsync(SalesShipmentsByPeriodRequest request) =>
        ToRows(await LoadAsync(request));

    public async Task<byte[]> ExecuteAsync(SalesShipmentsByPeriodRequest request)
    {
        var transactions = await LoadAsync(request);
        var rows = ToRows(transactions);
        var first = rows.Count > 0 ? rows[0] : null;
        var statuses = LogisticsReportText.EffectiveTransactionStatuses(request.Statuses);

        var extra = new List<string>();
        if (!string.IsNullOrWhiteSpace(request.WarehouseCode))
            extra.Add($"Armazém: {ReportText.Describe(first?.Warehouse, request.WarehouseCode)}");
        if (!string.IsNullOrWhiteSpace(request.TruckCode))
            extra.Add($"Placa: {request.TruckCode}");
        if (!string.IsNullOrWhiteSpace(request.CardCode))
            extra.Add($"Cliente: {ReportText.Describe(LogisticsReportText.CustomerName(transactions.Select(t => t.ShipmentLoad), request.CardCode), request.CardCode)}");
        if (request.HasLoad is { } hasLoad)
            extra.Add($"Vínculo com carga: {(hasLoad ? "Com carga" : "Sem carga")}");

        var parameters = new Dictionary<string, object>
        {
            ["pFilters"] = LogisticsReportText.BuildFilters(
                "Data do romaneio",
                request,
                await LogisticsReportText.BranchNameAsync(db.Context, request.BranchCode),
                ReportText.ProductOfGroup(first?.Group),
                LogisticsReportText.StatusFilter(statuses, LogisticsReportText.TransactionStatusText, StorageTransactionsStatus.Cancelled),
                extra),
            [FastReportService.SingleUomParameter] = ReportText.IsSingleUnit(rows.Select(r => r.UnitOfMeasure)),
        };

        return await reportService.GeneratePdfAsync(
            "SalesShipmentsByPeriod.frx", rows, "SalesShipments", "SalesShipments", parameters);
    }

    private async Task<List<StorageTransaction>> LoadAsync(SalesShipmentsByPeriodRequest request)
    {
        var from = request.FromDate.Date;
        var toExclusive = request.ToDate.Date.AddDays(1);
        var statuses = LogisticsReportText.EffectiveTransactionStatuses(request.Statuses);

        // Carga e notas da carga por Include (LEFT JOIN, FK opcional): o cliente sai das notas.
        var query = db.Context.StorageTransactions
            .AsNoTracking()
            .Include(t => t.ShipmentLoad).ThenInclude(l => l!.Invoices)
            .Where(t => t.TransactionType == StorageTransactionType.SalesShipment)
            .Where(t => t.TransactionDate >= from && t.TransactionDate < toExclusive)
            .Where(t => statuses.Contains(t.TransactionStatus));

        if (!string.IsNullOrWhiteSpace(request.BranchCode))
            query = query.Where(t => t.BranchCode == request.BranchCode);

        if (!string.IsNullOrWhiteSpace(request.ItemCode))
            query = query.Where(t => t.ItemCode == request.ItemCode);

        if (!string.IsNullOrWhiteSpace(request.WarehouseCode))
            query = query.Where(t => t.WarehouseCode == request.WarehouseCode);

        if (!string.IsNullOrWhiteSpace(request.TruckCode))
            query = query.Where(t => t.TruckCode == request.TruckCode);

        if (request.HasLoad is true)
            query = query.Where(t => t.ShipmentLoadKey != null);
        else if (request.HasLoad is false)
            query = query.Where(t => t.ShipmentLoadKey == null);

        var transactions = await query.ToListAsync();

        if (request.CardCode is { } cardCode && !string.IsNullOrWhiteSpace(cardCode))
            transactions = transactions.Where(t => LogisticsReportText.LoadHasCustomer(t.ShipmentLoad, cardCode)).ToList();

        return transactions;
    }

    private static List<SalesShipmentRowDto> ToRows(List<StorageTransaction> transactions)
    {
        var groupOf = InvoiceItemGrouping.BuildGroupResolver(
            transactions, t => t.ItemCode, t => t.ItemName, t => t.UnitOfMeasureCode);

        return transactions
            .OrderBy(t => groupOf(t), StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(t => t.TransactionDate)
            .ThenBy(t => t.Code ?? "", StringComparer.Ordinal)
            .Select(t => ToRow(t, groupOf(t)))
            .ToList();
    }

    private static SalesShipmentRowDto ToRow(StorageTransaction t, string group) => new()
    {
        Group = group,
        TransactionDate = ReportText.Date(t.TransactionDate),
        Code = t.Code ?? "",
        Load = t.ShipmentLoad?.Code ?? "",
        Truck = t.TruckCode ?? "",
        Supplier = ReportText.Partner(t.CardCode, t.CardName),
        Customers = LogisticsReportText.LoadCustomers(t.ShipmentLoad),
        Warehouse = ReportText.NameOrCode(t.WarehouseCode, t.WarehouseName),
        InvoiceNumber = ReportText.DocumentNumber(t.InvoiceNumber, t.InvoiceSerie),
        Status = LogisticsReportText.TransactionStatusText(t.TransactionStatus),
        UnitOfMeasure = t.UnitOfMeasureCode,
        GrossWeight = t.GrossWeight,
        DiscountWeight = t.DryingDiscount + t.CleaningDiscount + t.OthersDicount,
        NetWeight = t.NetWeight,
    };
}
