using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.Support;

/// <summary>Contratos e razão mínimos para os testes dos relatórios de posição de contratos.</summary>
public static class ContractPositionSeed
{
    /// <summary>"Hoje" fixo dos testes (a posição é calculada nesta data).</summary>
    public static readonly DateTime Today = new(2026, 10, 8);

    public static PurchaseContract Purchase(
        string code = "PC000001",
        decimal total = 100_000m,
        ContractStatus? status = ContractStatus.Approved,
        ContractType type = ContractType.Fixed,
        string cardCode = "F001",
        string cardName = "PRODUTOR RURAL",
        string itemCode = "10001",
        string itemName = "SOJA EM GRÃOS",
        string uom = "KG",
        string harvest = "25/26",
        string branchCode = "01",
        decimal price = 120.50m,
        DateTime? cashFlow = null,
        DateTime? deliveryEnd = null,
        DateTime? creation = null,
        decimal staleAllocated = 0m,
        decimal staleWashedOut = 0m) => new()
    {
        Key = Guid.NewGuid(),
        Code = code,
        CreationDate = creation ?? new DateTime(2026, 7, 1),
        Status = status,
        Type = type,
        BranchCode = branchCode,
        CardCode = cardCode,
        CardName = cardName,
        ItemCode = itemCode,
        ItemName = itemName,
        UnitOfMeasureCode = uom,
        HarvestSeasonCode = harvest,
        DeliveryLocationCode = "ARM01",
        DeliveryLocationName = "ARMAZÉM CENTRAL",
        DeliveryStartDate = new DateTime(2026, 7, 1),
        DeliveryEndDate = deliveryEnd ?? new DateTime(2026, 12, 31),
        StandardCashFlowDate = cashFlow,
        StandardPrice = price,
        TotalVolume = total,
        // Valores persistidos de propósito ERRADOS: o relatório nunca os lê.
        AllocatedVolume = staleAllocated,
        WashedOutVolume = staleWashedOut,
    };

    public static SalesContract Sales(
        string code = "CV000001",
        decimal total = 100_000m,
        ContractStatus? status = ContractStatus.Approved,
        ContractType type = ContractType.Fixed,
        string cardCode = "C001",
        string cardName = "COOPERATIVA CENTRAL",
        string itemCode = "10001",
        string itemName = "SOJA EM GRÃOS",
        string uom = "KG",
        string harvest = "25/26",
        string branchCode = "01",
        decimal price = 130.25m,
        DateTime? cashFlow = null,
        DateTime? deliveryEnd = null,
        DateTime? creation = null,
        decimal staleAllocated = 0m) => new()
    {
        Key = Guid.NewGuid(),
        Code = code,
        CreationDate = creation ?? new DateTime(2026, 7, 1),
        Status = status,
        Type = type,
        BranchCode = branchCode,
        CardCode = cardCode,
        CardName = cardName,
        ItemCode = itemCode,
        ItemName = itemName,
        UnitOfMeasureCode = uom,
        HarvestSeasonCode = harvest,
        DeliveryStartDate = new DateTime(2026, 7, 1),
        DeliveryEndDate = deliveryEnd ?? new DateTime(2026, 12, 31),
        StandardCashFlowDate = cashFlow,
        Price = price,
        TotalVolume = total,
        AllocatedVolume = staleAllocated,
    };

    /// <summary>Linha do razão de compra (romaneio alocado). Devolução = volume negativo.</summary>
    public static PurchaseContractAllocation PurchaseAllocation(PurchaseContract contract, decimal volume) => new()
    {
        Key = Guid.NewGuid(),
        PurchaseContractKey = contract.Key,
        StorageTransactionKey = Guid.NewGuid(),
        Volume = volume,
    };

    public static PurchaseContractWashout Washout(
        PurchaseContract contract,
        int sequence,
        decimal fixedVolume,
        decimal unfixedVolume = 0m,
        PurchaseContractWashoutStatus status = PurchaseContractWashoutStatus.Approved) => new()
    {
        Key = Guid.NewGuid(),
        PurchaseContractKey = contract.Key,
        Sequence = sequence,
        FixedVolume = fixedVolume,
        UnfixedVolume = unfixedVolume,
        Status = status,
    };

    /// <summary>Item de nota de venda. Entrega conferida = Closed, com entregue e perda.</summary>
    public static SalesInvoiceItem SalesItem(
        decimal quantity,
        bool closed = false,
        decimal delivered = 0m,
        decimal loss = 0m,
        string itemCode = "10001",
        string uom = "KG") => new()
    {
        Key = Guid.NewGuid(),
        ItemCode = itemCode,
        UnitOfMeasureCode = uom,
        Quantity = quantity,
        DeliveredQuantity = delivered,
        QuantityLoss = loss,
        DeliveryStatus = closed ? SalesInvoiceDeliveryStatus.Closed : SalesInvoiceDeliveryStatus.Open,
    };

    /// <summary>Linha do razão de venda. Devolução = volume negativo; a dona carrega a quebra.</summary>
    public static SalesContractAllocation SalesAllocation(
        SalesContract contract, SalesInvoiceItem item, decimal volume, bool owner = true) => new()
    {
        Key = Guid.NewGuid(),
        SalesContractKey = contract.Key,
        SalesInvoiceItemKey = item.Key!.Value,
        Volume = volume,
        OwnsDeliveryDifference = owner,
        Origin = SalesContractAllocationOrigin.Billing,
    };
}
