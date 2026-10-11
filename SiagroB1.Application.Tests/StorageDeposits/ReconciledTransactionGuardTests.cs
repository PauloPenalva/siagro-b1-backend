using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.ShippingTransactions;
using SiagroB1.Application.Services.StorageAddresses;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.StorageDeposits;

/// <summary>
/// O romaneio coberto por um retorno de depósito só sai de cena depois de desfeita a reconciliação:
/// cancelamento, estorno, estorno da Expedição e troca de liberação recusam.
/// </summary>
public class ReconciledTransactionGuardTests
{
    private const string Expected = "está reconciliado com o retorno de depósito";

    private static async Task<(UnitOfWork Db, StorageDepositSeed Seed, StorageTransaction Shipment)> ReconciledAsync()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var ret = await seed.AddReturnAsync(await seed.AddRemittanceAsync(100_000m), 30_000m);
        var shipment = await seed.AddShipmentAsync(30_000m, origin: TransactionCode.StorageTransaction);
        db.Context.StorageReturnReconciliations.Add(new StorageReturnReconciliation
            { PurchaseInvoiceKey = ret.Key, StorageTransactionKey = shipment.Key });
        await db.SaveChangesAsync();
        return (db, seed, shipment);
    }

    /// <summary>Expedição mínima: a Compra é o romaneio reconciliado; a saída está montada numa carga.</summary>
    private static async Task<(ShippingTransaction Shipping, StorageTransaction Sales)> ShippingAsync(
        UnitOfWork db, StorageDepositSeed seed, StorageTransaction purchase)
    {
        var load = new ShipmentLoad
        {
            Key = Guid.NewGuid(), Code = "CG000001", BranchCode = "01", ItemCode = "SOJA", UnitOfMeasureCode = "KG",
            TruckCode = "ABC1D23", WarehouseCode = StorageDepositSeed.Warehouse, LoadDate = new DateTime(2026, 9, 10),
            Status = ShipmentLoadStatus.Open,
        };
        var sales = await seed.AddShipmentAsync(30_000m, StorageTransactionType.SalesShipment);
        var shipping = new ShippingTransaction
        {
            PurchaseStorageTransactionKey = purchase.Key,
            SalesStorageTransactionKey = sales.Key,
        };
        db.Context.ShipmentLoads.Add(load);
        db.Context.ShippingTransactions.Add(shipping);
        await db.SaveChangesAsync();
        return (shipping, sales);
    }

    [Fact]
    public async Task Cancel_refuses_reconciled_transaction()
    {
        var (db, _, shipment) = await ReconciledAsync();
        var service = new StorageTransactionsCancelService(db, new ShipmentReleasesRecalculateShippedService(db.Context));

        var e = await Assert.ThrowsAsync<ApplicationException>(() => service.ExecuteAsync(shipment.Key, "tester"));

        Assert.Contains(Expected, e.Message);
    }

    [Fact]
    public async Task Reverse_refuses_reconciled_transaction()
    {
        var (db, _, shipment) = await ReconciledAsync();
        var service = new StorageTransactionsReverseService(
            db,
            new StorageAddressesGetBalanceService(null!),
            new ShipmentReleasesRecalculateShippedService(db.Context),
            new FakeStringLocalizer<Resource>());

        var e = await Assert.ThrowsAsync<ApplicationException>(() => service.ExecuteAsync(shipment.Key, "tester"));

        Assert.Contains(Expected, e.Message);
    }

    [Fact]
    public async Task Shipping_reverse_refuses_reconciled_purchase()
    {
        var (db, seed, shipment) = await ReconciledAsync();
        var (_, sales) = await ShippingAsync(db, seed, shipment);
        var service = new ShippingTransactionsReverseService(
            db,
            new StorageTransactionsGetService(db, NullLogger<StorageTransactionsGetService>.Instance),
            new PurchaseContractsAllocationDeleteService(db, NullLogger<PurchaseContractsAllocationDeleteService>.Instance),
            new ShipmentReleasesRecalculateShippedService(db.Context),
            NullLogger<ShippingTransactionsReverseService>.Instance);

        var e = await Assert.ThrowsAsync<ApplicationException>(() => service.ExecuteAsync(sales.Key, "tester"));

        Assert.Contains(Expected, e.Message);
    }

    [Fact]
    public async Task Release_change_refuses_reconciled_purchase()
    {
        var (db, seed, shipment) = await ReconciledAsync();
        var (shipping, sales) = await ShippingAsync(db, seed, shipment);

        // A saída está numa carga aberta e na liberação do seed; o destino é outra liberação ativa do mesmo produto.
        var load = db.Context.ShipmentLoads.Single();
        sales.ShipmentLoadKey = load.Key;
        var targetContract = new PurchaseContract
        {
            Key = Guid.NewGuid(), Code = "00000002", BranchCode = StorageDepositSeed.Branch, CardCode = "F0002",
            ItemCode = "SOJA", UnitOfMeasureCode = "KG", HarvestSeasonCode = "2526",
            DeliveryLocationCode = StorageDepositSeed.Warehouse, Status = ContractStatus.Approved,
        };
        var target = new ShipmentRelease
        {
            Key = Guid.NewGuid(), BranchCode = StorageDepositSeed.Branch, PurchaseContractKey = targetContract.Key,
            ReleasedQuantity = 100_000m, DeliveryLocationCode = StorageDepositSeed.Warehouse,
            DeliveryLocationName = "ARMAZEM TERCEIRO", Status = ReleaseStatus.Actived,
        };
        db.Context.PurchaseContracts.Add(targetContract);
        db.Context.ShipmentReleases.Add(target);
        await db.SaveChangesAsync();

        var docNumbers = new FakeDocNumberSequenceService();
        var guard = new ShipmentReleaseMovementGuardService(db.Context);
        var recalc = new ShipmentReleasesRecalculateShippedService(db.Context);
        var storageCreate = new StorageTransactionsCreateService(
            db, docNumbers,
            new FakeBusinessPartnerService(new() { ["F0001"] = "Produtor Um", ["F0002"] = "Produtor Dois" }),
            new FakeItemService(new() { ["SOJA"] = "SOJA EM GRAOS" }),
            new FakeWarehouseService(new() { [StorageDepositSeed.Warehouse] = "Armazém" }),
            recalc, guard, NullLogger<StorageTransactionsCreateService>.Instance);
        var service = new ShippingTransactionsChangeReleaseService(
            db,
            storageCreate,
            new StorageTransactionsConfirmedService(
                db, new FakeStringLocalizer<Resource>(), recalc, guard,
                NullLogger<StorageTransactionsConfirmedService>.Instance),
            new PurchaseContractsAllocationCreateService(
                db, new StorageTransactionsGetService(db, NullLogger<StorageTransactionsGetService>.Instance)),
            recalc,
            new ShipmentLoadsMovementLogService(db.Context),
            new ShippingReleaseChangeReconciliationGuard(db.Context),
            new FakeStorageAddressBalanceReader(100_000m));

        var e = await Assert.ThrowsAsync<ApplicationException>(() =>
            service.ExecuteAsync([new(shipping.SalesStorageTransactionKey, target.Key)], "motivo", "tester"));

        Assert.Contains(Expected, e.Message);
    }
}
