using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Services.PurchaseDischarges;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseDischarges;

/// <summary>
/// Monta uma carga faturada numa nota Normal, com N Expedições (par Compra 8 alocada + Saída 7 na
/// carga + linha em SHIPPING_TRANSACTIONS), cada uma no seu contrato de compra.
/// </summary>
internal sealed class PurchaseDischargeTestContext
{
    public UnitOfWork Db { get; } = TestDb.CreateUnitOfWork();
    public ShipmentLoad Load { get; private set; } = null!;
    public SalesInvoice Invoice { get; private set; } = null!;
    public SalesInvoiceItem Item { get; private set; } = null!;
    private int _seq;

    public StorageTransactionsCreateService StorageCreate() =>
        new(Db,
            new FakeDocNumberSequenceService(),
            new FakeBusinessPartnerService(new() { ["F0001"] = "Cerealista A", ["F0002"] = "Cerealista B" }),
            new FakeItemService(new() { ["SOJA"] = "SOJA EM GRAOS" }),
            new FakeWarehouseService(new() { ["PORTO"] = "Porto" }),
            new ShipmentReleasesRecalculateShippedService(Db.Context),
            new ShipmentReleaseMovementGuardService(Db.Context),
            NullLogger<StorageTransactionsCreateService>.Instance);

    public PurchaseDischargeTransactionsSyncService Sync() =>
        new(Db,
            StorageCreate(),
            new PurchaseContractsAllocationCreateService(Db,
                new StorageTransactionsGetService(Db, NullLogger<StorageTransactionsGetService>.Instance)),
            new PurchaseContractsAllocationDeleteService(Db, NullLogger<PurchaseContractsAllocationDeleteService>.Instance));

    public async Task SeedLoadAndInvoiceAsync(decimal itemQuantity)
    {
        Load = new ShipmentLoad
        {
            Key = Guid.NewGuid(), Code = "CG000001", BranchCode = "01", ItemCode = "SOJA",
            ItemName = "SOJA EM GRAOS", UnitOfMeasureCode = "KG", TruckCode = "ABC1D23",
            WarehouseCode = "PORTO", LoadType = ShipmentLoadType.Normal, Status = ShipmentLoadStatus.Invoiced,
        };
        Invoice = new SalesInvoice
        {
            Key = Guid.NewGuid(), CardCode = "C0001", InvoiceNumber = "NF-1", ShipmentLoadKey = Load.Key,
            InvoiceStatus = InvoiceStatus.Confirmed, InvoiceType = SalesInvoiceType.Normal,
        };
        Item = new SalesInvoiceItem
        {
            Key = Guid.NewGuid(), SalesInvoiceKey = Invoice.Key, ItemCode = "SOJA", UnitOfMeasureCode = "KG",
            Quantity = itemQuantity,
        };
        Db.Context.ShipmentLoads.Add(Load);
        Db.Context.SalesInvoices.Add(Invoice);
        Db.Context.SalesInvoicesItems.Add(Item);
        await Db.Context.SaveChangesAsync();
    }

    public async Task<PurchaseContract> SeedContractAsync(
        FreightTerms freight = FreightTerms.Cif, decimal total = 10_000m,
        ContractStatus status = ContractStatus.Approved, string cardCode = "F0001")
    {
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(), Code = $"PC-{++_seq}", CardCode = cardCode, ItemCode = "SOJA",
            UnitOfMeasureCode = "KG", HarvestSeasonCode = "24/25", Status = status,
            TotalVolume = total, FreightTerms = freight, DeliveryLocationCode = "01",
        };
        Db.Context.PurchaseContracts.Add(contract);
        await Db.Context.SaveChangesAsync();
        return contract;
    }

    /// <summary>O que a Expedição grava: 8 alocada no contrato + 7 na carga + par em SHIPPING_TRANSACTIONS.</summary>
    public async Task<ShippingTransaction> SeedExpeditionAsync(PurchaseContract contract, decimal weight)
    {
        var purchase = NewTx(StorageTransactionType.Purchase, contract.CardCode, weight);
        var sales = NewTx(StorageTransactionType.SalesShipment, contract.CardCode, weight);
        sales.ShipmentLoadKey = Load.Key;
        var shipping = new ShippingTransaction
        {
            Key = Guid.NewGuid(), PurchaseStorageTransactionKey = purchase.Key, SalesStorageTransactionKey = sales.Key,
        };

        Db.Context.StorageTransactions.AddRange(purchase, sales);
        Db.Context.ShippingTransactions.Add(shipping);
        Db.Context.PurchaseContractsAllocations.Add(new PurchaseContractAllocation
        {
            Key = Guid.NewGuid(), PurchaseContractKey = contract.Key, StorageTransactionKey = purchase.Key, Volume = weight,
        });
        var tracked = await Db.Context.PurchaseContracts.SingleAsync(x => x.Key == contract.Key);
        tracked.AllocatedVolume += weight;
        await Db.Context.SaveChangesAsync();
        return shipping;
    }

    private StorageTransaction NewTx(StorageTransactionType type, string cardCode, decimal weight) => new()
    {
        Key = Guid.NewGuid(), Code = $"SEED-{++_seq:0000}", BranchCode = "01", CardCode = cardCode,
        ItemCode = "SOJA", UnitOfMeasureCode = "KG", WarehouseCode = "PORTO", TruckCode = "ABC1D23",
        TransactionType = type, TransactionStatus = StorageTransactionsStatus.Confirmed,
        GrossWeight = weight, NetWeight = weight,
    };

    public Task<List<StorageTransaction>> DischargesAsync() =>
        Db.Context.StorageTransactions.AsNoTracking()
            .Where(x => x.TransactionType == StorageTransactionType.PurchaseDischargeLoss ||
                        x.TransactionType == StorageTransactionType.PurchaseDischargeGain)
            .ToListAsync();

    public async Task<decimal> AllocatedAsync(Guid contractKey) =>
        (await Db.Context.PurchaseContracts.AsNoTracking().SingleAsync(x => x.Key == contractKey)).AllocatedVolume;

    /// <summary>Atalho: Prepare + Apply como o gancho faz.</summary>
    public async Task SyncAsync(SalesInvoiceDeliveryStatus status, decimal delivered, decimal loss = 0m)
    {
        var sync = Sync();
        var plan = await sync.PrepareAsync(Item.Key!.Value, status, Item.Quantity, delivered, loss);
        await sync.ApplyAsync(plan, "tester");
    }
}
