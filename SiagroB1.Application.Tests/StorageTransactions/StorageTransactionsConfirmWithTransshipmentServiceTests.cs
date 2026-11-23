using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.StorageTransactions;

/// <summary>
/// GAC-1232 — a Yokotobi não usa o módulo de Pesagem: a saída do lote de transbordo em armazém
/// PRÓPRIO é lançada pela tela Romaneios de Movimentação e confirmada pela action
/// <c>StorageTransactionsConfirmed</c>. Até aqui só a conclusão do ticket de pesagem emitia a
/// liberação do transbordo; confirmar o romaneio à mão baixava o lote e deixava a carga sem
/// liberação, invisível na Expedição.
/// </summary>
/// <remarks>
/// As regras do vínculo (quantidade real, liberação sem lote, recusas por caminhão) continuam
/// cobertas por <c>ShipmentLoadsTransshipmentAttachLotExitServiceTests</c> e
/// <c>WeighingTicketTransshipmentLotExitTriggerTests</c>. Esta classe cobre só o GATILHO da
/// confirmação manual.
/// </remarks>
public class StorageTransactionsConfirmWithTransshipmentServiceTests
{
    private const string TransshipmentWarehouse = "ARM99";
    private const string OriginWarehouse = "ARM01";
    private const string CardCode = "C0001";
    private const string SupplierCardCode = "F0001";
    private const string ItemCode = "SOJA";
    private const string LotCode = "L-TRANSSHIP-01";
    private const string BranchCode = "01";
    private const string TruckCode = "ABC1D23";
    private const string OtherTruckCode = "XYZ9W88";

    private static StorageTransactionsConfirmWithTransshipmentService Service(IUnitOfWork db) => new(
        db,
        new StorageTransactionsConfirmedService(
            db,
            new FakeStringLocalizer<Resource>(),
            new ShipmentReleasesRecalculateShippedService(db.Context),
            new ShipmentReleaseMovementGuardService(db.Context),
            NullLogger<StorageTransactionsConfirmedService>.Instance),
        new ShipmentLoadsTransshipmentAttachLotExitService(
            db,
            new FakeWarehouseService(new() { [TransshipmentWarehouse] = "Armazém Transbordo" }),
            new ShipmentReleasesFromReturnService(db.Context),
            new ShipmentLoadsMovementLogService(db.Context)));

    private static async Task<IUnitOfWork> SeedLotAsync(
        StorageAddressNature nature = StorageAddressNature.Transshipment)
    {
        var db = TestDb.CreateUnitOfWork();

        db.Context.StorageAddresses.Add(new StorageAddress
        {
            Code = LotCode,
            Description = "Lote de transbordo",
            CardCode = CardCode,
            ItemCode = ItemCode,
            WarehouseCode = TransshipmentWarehouse,
            UoM = "KG",
            Nature = nature,
        });

        await db.SaveChangesAsync();
        return db;
    }

    private static async Task<Guid> SeedOriginReleaseAsync(IUnitOfWork db)
    {
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(),
            Code = "PC-001",
            CardCode = SupplierCardCode,
            ItemCode = ItemCode,
            UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "2026",
            DeliveryLocationCode = OriginWarehouse,
            BranchCode = BranchCode,
            Status = ContractStatus.Approved,
            TotalVolume = 1_000_000m,
        };
        var release = new ShipmentRelease
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = contract.Key,
            DeliveryLocationCode = OriginWarehouse,
            ReleasedQuantity = 1_000_000m,
            Status = ReleaseStatus.Actived,
        };
        db.Context.PurchaseContracts.Add(contract);
        db.Context.ShipmentReleases.Add(release);
        await db.SaveChangesAsync();
        return release.Key;
    }

    /// <summary>Carga em transbordo com a entrada já registrada no lote (estado da Task 3).</summary>
    private static async Task SeedOpenTransshipmentAsync(
        IUnitOfWork db, decimal entryQuantity, Guid? originReleaseKey)
    {
        var load = new ShipmentLoad
        {
            Key = Guid.NewGuid(),
            Code = "CG000354",
            BranchCode = BranchCode,
            ItemCode = ItemCode,
            ItemName = "Soja em grãos",
            UnitOfMeasureCode = "KG",
            TruckCode = TruckCode,
            WarehouseCode = OriginWarehouse,
            Status = ShipmentLoadStatus.InTransshipment,
            TotalQuantity = entryQuantity,
        };

        var origin = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            CardCode = SupplierCardCode,
            ItemCode = ItemCode,
            UnitOfMeasureCode = "KG",
            WarehouseCode = OriginWarehouse,
            BranchCode = BranchCode,
            TruckCode = TruckCode,
            GrossWeight = entryQuantity,
            NetWeight = entryQuantity,
            TransactionType = StorageTransactionType.SalesShipment,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
            ShipmentLoadKey = load.Key,
            ShipmentReleaseKey = originReleaseKey,
        };

        var entryReceipt = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            CardCode = SupplierCardCode,
            ItemCode = ItemCode,
            UnitOfMeasureCode = "KG",
            WarehouseCode = TransshipmentWarehouse,
            BranchCode = BranchCode,
            StorageAddressCode = LotCode,
            GrossWeight = entryQuantity,
            NetWeight = entryQuantity,
            TransactionType = StorageTransactionType.Receipt,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
        };

        var transshipment = new ShipmentLoadTransshipment
        {
            ShipmentLoadKey = load.Key,
            Sequence = 1,
            WarehouseCode = TransshipmentWarehouse,
            WarehouseName = "Armazém Transbordo",
            OutgoingQuantity = entryQuantity,
            EntryQuantity = entryQuantity,
        };

        db.Context.ShipmentLoads.Add(load);
        db.Context.StorageTransactions.Add(origin);
        db.Context.StorageTransactions.Add(entryReceipt);
        db.Context.ShipmentLoadsTransshipments.Add(transshipment);
        await db.SaveChangesAsync();

        entryReceipt.ShipmentLoadTransshipmentKey = transshipment.Key;
        transshipment.EntryStorageTransactionKey = entryReceipt.Key;
        await db.SaveChangesAsync();
    }

    /// <summary>Romaneio de Saída lançado à mão (Romaneios de Movimentação), ainda pendente.</summary>
    private static async Task<StorageTransaction> SeedPendingLotExitAsync(
        IUnitOfWork db, string truckCode, decimal grossWeight,
        StorageTransactionType type = StorageTransactionType.Shipment)
    {
        var st = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = "00014662",
            CardCode = CardCode,
            ItemCode = ItemCode,
            UnitOfMeasureCode = "KG",
            WarehouseCode = TransshipmentWarehouse,
            BranchCode = BranchCode,
            StorageAddressCode = LotCode,
            TruckCode = truckCode,
            GrossWeight = grossWeight,
            TransactionType = type,
            TransactionStatus = StorageTransactionsStatus.Pending,
            TransactionOrigin = TransactionCode.StorageTransaction,
        };
        db.Context.StorageTransactions.Add(st);
        await db.SaveChangesAsync();
        return st;
    }

    [Fact]
    public async Task ManualExit_InTransshipmentLot_SameTruck_EmitsReleaseForTheRealQuantity()
    {
        var db = await SeedLotAsync();
        var originReleaseKey = await SeedOriginReleaseAsync(db);
        await SeedOpenTransshipmentAsync(db, entryQuantity: 50_000m, originReleaseKey);
        var lotExit = await SeedPendingLotExitAsync(db, TruckCode, grossWeight: 49_000m);

        await Service(db).ExecuteAsync(lotExit.Key, "tester");

        var confirmed = await db.Context.StorageTransactions.AsNoTracking()
            .SingleAsync(x => x.Key == lotExit.Key);
        Assert.Equal(StorageTransactionsStatus.Confirmed, confirmed.TransactionStatus);
        Assert.NotNull(confirmed.ShipmentLoadTransshipmentKey);

        var release = await db.Context.ShipmentReleases.AsNoTracking()
            .SingleAsync(x => x.Origin == ReleaseOrigin.Transshipment);
        Assert.Equal(49_000m, release.ReleasedQuantity);
        Assert.Null(release.StorageAddressCode);
        Assert.Equal(lotExit.Key, release.GeneratedByStorageTransactionKey);

        var transshipment = await db.Context.ShipmentLoadsTransshipments.AsNoTracking().SingleAsync();
        Assert.Equal(lotExit.Key, transshipment.LotExitStorageTransactionKey);
    }

    [Fact]
    public async Task ManualExit_InTransshipmentLot_DifferentTruck_Refuses()
    {
        var db = await SeedLotAsync();
        await SeedOpenTransshipmentAsync(db, entryQuantity: 50_000m, originReleaseKey: null);
        var lotExit = await SeedPendingLotExitAsync(db, OtherTruckCode, grossWeight: 49_000m);

        var error = await Assert.ThrowsAnyAsync<Exception>(
            () => Service(db).ExecuteAsync(lotExit.Key, "tester"));

        Assert.Contains("não tem entrada de transbordo registrada", error.Message);
        Assert.Empty(await db.Context.ShipmentReleases
            .Where(x => x.Origin == ReleaseOrigin.Transshipment).ToListAsync());
    }

    /// <summary>Não-regressão: saída em lote COMUM confirma como sempre, sem procurar transbordo.</summary>
    [Fact]
    public async Task ManualExit_InRegularLot_ConfirmsWithoutRelease()
    {
        var db = await SeedLotAsync(StorageAddressNature.Regular);
        var lotExit = await SeedPendingLotExitAsync(db, TruckCode, grossWeight: 49_000m);

        await Service(db).ExecuteAsync(lotExit.Key, "tester");

        var confirmed = await db.Context.StorageTransactions.AsNoTracking()
            .SingleAsync(x => x.Key == lotExit.Key);
        Assert.Equal(StorageTransactionsStatus.Confirmed, confirmed.TransactionStatus);
        Assert.Null(confirmed.ShipmentLoadTransshipmentKey);
        Assert.Empty(await db.Context.ShipmentReleases.ToListAsync());
    }

    /// <summary>Não-regressão: ENTRADA no lote de transbordo não é saída do lote.</summary>
    [Fact]
    public async Task ManualReceipt_InTransshipmentLot_ConfirmsWithoutRelease()
    {
        var db = await SeedLotAsync();
        var receipt = await SeedPendingLotExitAsync(
            db, TruckCode, grossWeight: 49_000m, type: StorageTransactionType.Receipt);

        await Service(db).ExecuteAsync(receipt.Key, "tester");

        var confirmed = await db.Context.StorageTransactions.AsNoTracking()
            .SingleAsync(x => x.Key == receipt.Key);
        Assert.Equal(StorageTransactionsStatus.Confirmed, confirmed.TransactionStatus);
        Assert.Empty(await db.Context.ShipmentReleases.ToListAsync());
    }
}
