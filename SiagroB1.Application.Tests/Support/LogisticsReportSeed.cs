using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.Support;

/// <summary>Documentos mínimos para os testes dos relatórios de logística de venda.</summary>
public static class LogisticsReportSeed
{
    public static readonly DateTime Jul01 = new(2026, 7, 1);
    public static readonly DateTime Jul15 = new(2026, 7, 15);
    public static readonly DateTime Jul31 = new(2026, 7, 31);

    public static ShipmentLoad Load(
        string code,
        DateTime? date = null,
        ShipmentLoadStatus status = ShipmentLoadStatus.Open,
        ShipmentLoadType type = ShipmentLoadType.Normal,
        string itemCode = "10001",
        string itemName = "SOJA EM GRÃOS",
        string uom = "KG",
        decimal total = 30_000m,
        string branchCode = "01",
        string truckCode = "ABC1D23",
        string warehouseCode = "ARM01",
        string carrierCardCode = "T001",
        string? plannedCardCode = null,
        string? plannedCardName = null) => new()
    {
        Key = Guid.NewGuid(),
        Code = code,
        LoadDate = date ?? Jul15,
        Status = status,
        LoadType = type,
        ItemCode = itemCode,
        ItemName = itemName,
        UnitOfMeasureCode = uom,
        TotalQuantity = total,
        BranchCode = branchCode,
        TruckCode = truckCode,
        WarehouseCode = warehouseCode,
        WarehouseName = "ARMAZÉM CENTRAL",
        CarrierCardCode = carrierCardCode,
        CarrierName = "TRANSPORTES RÁPIDO",
        CardCode = plannedCardCode,
        CardName = plannedCardName,
    };

    /// <summary>Nota de saída ligada à carga (o cliente da carga sai daqui).</summary>
    public static SalesInvoice LoadInvoice(
        ShipmentLoad load,
        string number,
        string cardCode = "C001",
        string cardName = "COOPERATIVA CENTRAL",
        InvoiceStatus? status = InvoiceStatus.Confirmed) => new()
    {
        Key = Guid.NewGuid(),
        InvoiceNumber = number,
        InvoiceDate = load.LoadDate,
        InvoiceStatus = status,
        InvoiceType = SalesInvoiceType.Normal,
        BranchCode = load.BranchCode,
        CardCode = cardCode,
        CardName = cardName,
        ShipmentLoadKey = load.Key,
    };

    public static SalesContract SalesContract(
        string code = "CV000001",
        string cardCode = "C001",
        string cardName = "COOPERATIVA CENTRAL",
        string itemCode = "10001",
        string itemName = "SOJA EM GRÃOS",
        string uom = "KG",
        int? agentCode = 7,
        string? agentName = "JOÃO VENDEDOR",
        string? regionCode = "R01",
        string branchCode = "01") => new()
    {
        Key = Guid.NewGuid(),
        Code = code,
        BranchCode = branchCode,
        CardCode = cardCode,
        CardName = cardName,
        ItemCode = itemCode,
        ItemName = itemName,
        UnitOfMeasureCode = uom,
        HarvestSeasonCode = "25/26",
        AgentCode = agentCode,
        AgentName = agentName,
        LogisticRegionCode = regionCode,
        DeliveryStartDate = Jul01,
        DeliveryEndDate = new DateTime(2026, 9, 30),
        TotalVolume = 1_000_000m,
    };

    public static LogisticRegion Region(string code = "R01", string name = "NORTE") => new() { Code = code, Name = name };

    public static SalesShipmentRelease SalesRelease(
        SalesContract contract,
        DateTime? date = null,
        decimal released = 1_000m,
        decimal shipped = 0m,
        ReleaseStatus status = ReleaseStatus.Actived,
        string deliveryLocationCode = "L01",
        string? deliveryLocationName = "PORTO DE PARANAGUÁ",
        string branchCode = "01") => new()
    {
        Key = Guid.NewGuid(),
        SalesContractKey = contract.Key,
        ReleaseDate = date ?? Jul15,
        ReleasedQuantity = released,
        ShippedQuantity = shipped,
        Status = status,
        DeliveryLocationCode = deliveryLocationCode,
        DeliveryLocationName = deliveryLocationName,
        BranchCode = branchCode,
    };

    public static PurchaseContract PurchaseContract(
        string code = "PC000001",
        string cardCode = "F001",
        string cardName = "PRODUTOR RURAL",
        string itemCode = "10001",
        string itemName = "SOJA EM GRÃOS",
        string uom = "KG",
        string branchCode = "01") => new()
    {
        Key = Guid.NewGuid(),
        Code = code,
        BranchCode = branchCode,
        CardCode = cardCode,
        CardName = cardName,
        ItemCode = itemCode,
        ItemName = itemName,
        UnitOfMeasureCode = uom,
        HarvestSeasonCode = "25/26",
        DeliveryLocationCode = "ARM01",
        DeliveryLocationName = "ARMAZÉM CENTRAL",
        DeliveryStartDate = Jul01,
        DeliveryEndDate = new DateTime(2026, 9, 30),
        TotalVolume = 1_000_000m,
    };

    public static ShipmentRelease PurchaseRelease(
        PurchaseContract contract,
        DateTime? date = null,
        decimal released = 1_000m,
        decimal shipped = 0m,
        ReleaseStatus status = ReleaseStatus.Actived,
        ReleaseOrigin origin = ReleaseOrigin.Standard,
        string deliveryLocationCode = "ARM01",
        string? deliveryLocationName = "ARMAZÉM CENTRAL",
        string branchCode = "01") => new()
    {
        Key = Guid.NewGuid(),
        PurchaseContractKey = contract.Key,
        ReleaseDate = date ?? Jul15,
        ReleasedQuantity = released,
        ShippedQuantity = shipped,
        Status = status,
        Origin = origin,
        DeliveryLocationCode = deliveryLocationCode,
        DeliveryLocationName = deliveryLocationName,
        BranchCode = branchCode,
    };

    /// <summary>
    /// Romaneio de venda (SalesShipment = 7). <c>CardCode</c> é o FORNECEDOR da perna de compra;
    /// o cliente chega pela carga.
    /// </summary>
    public static StorageTransaction SalesShipment(
        string code,
        DateTime? date = null,
        StorageTransactionsStatus status = StorageTransactionsStatus.Confirmed,
        ShipmentLoad? load = null,
        string itemCode = "10001",
        string itemName = "SOJA EM GRÃOS",
        string uom = "KG",
        decimal gross = 30_000m,
        decimal drying = 0m,
        decimal cleaning = 0m,
        decimal others = 0m,
        string branchCode = "01",
        string warehouseCode = "ARM01",
        string truckCode = "ABC1D23",
        string cardCode = "F001",
        string cardName = "PRODUTOR RURAL") => new()
    {
        Key = Guid.NewGuid(),
        Code = code,
        TransactionDate = date ?? Jul15,
        TransactionType = StorageTransactionType.SalesShipment,
        TransactionStatus = status,
        ShipmentLoadKey = load?.Key,
        ItemCode = itemCode,
        ItemName = itemName,
        UnitOfMeasureCode = uom,
        GrossWeight = gross,
        DryingDiscount = drying,
        CleaningDiscount = cleaning,
        OthersDicount = others,
        NetWeight = gross - drying - cleaning - others,
        BranchCode = branchCode,
        WarehouseCode = warehouseCode,
        WarehouseName = "ARMAZÉM CENTRAL",
        TruckCode = truckCode,
        CardCode = cardCode,
        CardName = cardName,
        InvoiceNumber = "000123",
        InvoiceSerie = "1",
    };
}
