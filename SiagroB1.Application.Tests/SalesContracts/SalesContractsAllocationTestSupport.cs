using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesContracts;

/// <summary>
/// Seeds compartilhados das suítes do ledger de alocações de venda.
/// </summary>
internal static class SalesContractsAllocationTestSupport
{
    internal static SalesContract NewContract(
        decimal totalVolume, decimal price = 100m,
        ContractStatus status = ContractStatus.Approved,
        string cardCode = "C0001", string itemCode = "SOJA", string uom = "KG") => new()
    {
        Key = Guid.NewGuid(),
        Code = Guid.NewGuid().ToString("N")[..8],
        CardCode = cardCode,
        ItemCode = itemCode,
        UnitOfMeasureCode = uom,
        HarvestSeasonCode = "24/25",
        TotalVolume = totalVolume,
        Price = price,
        Status = status,
    };

    internal static SalesShipmentRelease NewRelease(
        Guid contractKey, decimal released, decimal shipped = 0m,
        ReleaseStatus status = ReleaseStatus.Actived) => new()
    {
        Key = Guid.NewGuid(),
        SalesContractKey = contractKey,
        DeliveryLocationCode = "01",
        ReleasedQuantity = released,
        ShippedQuantity = shipped,
        Status = status,
    };

    internal static SalesInvoice NewInvoice(
        InvoiceStatus status = InvoiceStatus.Confirmed,
        SalesInvoiceType type = SalesInvoiceType.Normal,
        string cardCode = "C0001",
        Guid? originKey = null) => new()
    {
        Key = Guid.NewGuid(),
        CardCode = cardCode,
        InvoiceStatus = status,
        InvoiceType = type,
        SalesInvoiceOriginKey = originKey,
    };

    internal static SalesInvoiceItem NewItem(
        SalesInvoice invoice, Guid? contractKey, Guid? releaseKey,
        decimal quantity, decimal unitPrice = 90m,
        Guid? originItemKey = null, string itemCode = "SOJA", string uom = "KG")
    {
        var item = new SalesInvoiceItem
        {
            Key = Guid.NewGuid(),
            SalesInvoiceKey = invoice.Key,
            ItemCode = itemCode,
            UnitOfMeasureCode = uom,
            Quantity = quantity,
            UnitPrice = unitPrice,
            SalesContractKey = contractKey,
            SalesShipmentReleaseKey = releaseKey,
            SalesInvoiceItemOriginKey = originItemKey,
        };
        invoice.Items.Add(item);
        return item;
    }

    internal static SalesContractAllocation NewAllocation(
        Guid contractKey, Guid itemKey, decimal volume, Guid? releaseKey = null,
        SalesContractAllocationOrigin origin = SalesContractAllocationOrigin.Billing,
        decimal invoiceUnitPrice = 90m, decimal contractPrice = 100m,
        Guid? groupKey = null) => new()
    {
        Key = Guid.NewGuid(),
        SalesContractKey = contractKey,
        SalesInvoiceItemKey = itemKey,
        SalesShipmentReleaseKey = releaseKey,
        Volume = volume,
        InvoiceUnitPrice = invoiceUnitPrice,
        ContractPrice = contractPrice,
        PriceDifference = decimal.Round(volume * (invoiceUnitPrice - contractPrice), 2),
        Origin = origin,
        ReallocationGroupKey = groupKey,
    };

    internal static async Task<SalesContract> ContractAsync(UnitOfWork db, Guid key) =>
        await db.Context.SalesContracts.AsNoTracking().SingleAsync(c => c.Key == key);

    internal static async Task<SalesShipmentRelease> ReleaseAsync(UnitOfWork db, Guid key) =>
        await db.Context.SalesShipmentReleases.AsNoTracking().SingleAsync(r => r.Key == key);
}
