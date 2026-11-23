using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using static SiagroB1.Application.Tests.ShipmentLoads.ShipmentLoadsTransferTestData;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// Transferência que termina em armazém PRÓPRIO (GAC-1232): usa o caminho do armazém de terceiro —
/// entrada pesada, romaneio 15 creditando o armazém e liberação sem lote. Só o transbordo comum
/// continua vinculando a Entrada em Armazenagem do lote.
/// </summary>
public class ShipmentLoadsOwnWarehouseTransferTests
{
    private const string CardCode = "C0001";

    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private static FakeWarehouseService Warehouses() =>
        new(new Dictionary<string, string>
        {
            [OriginWarehouse] = "ARMAZEM ORIGEM",
            [DestinationWarehouse] = "ARMAZEM DESTINO",
        });

    private ShipmentLoadsTransshipmentRegisterEntryService RegisterService() => new(
        _db,
        Warehouses(),
        new WarehouseComplementService(_db),
        new StorageTransactionsCreateService(
            _db,
            new FakeDocNumberSequenceService(),
            new FakeBusinessPartnerService(names: new Dictionary<string, string> { [CardCode] = "CLIENTE TESTE" }),
            new FakeItemService(new Dictionary<string, string> { ["SOJA"] = "SOJA EM GRAOS" }),
            Warehouses(),
            new ShipmentReleasesRecalculateShippedService(_db.Context),
            new ShipmentReleaseMovementGuardService(_db.Context),
            NullLogger<StorageTransactionsCreateService>.Instance),
        new StorageTransactionsConfirmedService(
            _db,
            new FakeStringLocalizer<Resource>(),
            new ShipmentReleasesRecalculateShippedService(_db.Context),
            new ShipmentReleaseMovementGuardService(_db.Context),
            NullLogger<StorageTransactionsConfirmedService>.Instance),
        new ShipmentReleasesFromReturnService(_db.Context),
        new ShipmentLoadsMovementLogService(_db.Context));

    /// <summary>
    /// Carga em transferência para armazém próprio, com a origem ligada a uma liberação do
    /// contrato PC-001 (o rastreio que a liberação do destino precisa).
    /// </summary>
    private async Task<(ShipmentLoad Load, ShipmentLoadTransshipment Transshipment, PurchaseContract Contract)>
        SeedAsync()
    {
        _db.Context.WarehouseComplements.Add(new WarehouseComplement
        {
            WarehouseCode = DestinationWarehouse,
            IsOwn = true,
        });

        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(),
            Code = "PC-001",
            CardCode = "F0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "2026",
            DeliveryLocationCode = OriginWarehouse,
            BranchCode = "01",
            Status = ContractStatus.Approved,
            TotalVolume = 1_000_000m,
        };
        _db.Context.PurchaseContracts.Add(contract);

        var originRelease = new ShipmentRelease
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = contract.Key,
            DeliveryLocationCode = OriginWarehouse,
            ReleasedQuantity = 1_000_000m,
            Status = ReleaseStatus.Actived,
        };
        _db.Context.ShipmentReleases.Add(originRelease);

        var load = Load(_db);
        await _db.Context.SaveChangesAsync();

        var origin = await _db.Context.StorageTransactions
            .SingleAsync(x => x.ShipmentLoadKey == load.Key);
        origin.ShipmentReleaseKey = originRelease.Key;

        var transshipment = await TransshipmentAsync(_db, load, type: TransshipmentType.Transfer);
        return (load, transshipment, contract);
    }

    [Fact]
    public async Task RegisterEntry_TransferToOwnWarehouse_UsesTheWeighedQuantity()
    {
        var (_, transshipment, _) = await SeedAsync();

        await RegisterService().ExecuteAsync(
            transshipment.Key!.Value, 29_800m, DateTime.Today, null, "tester");

        var entry = await _db.Context.StorageTransactions.AsNoTracking()
            .SingleAsync(x => x.TransactionType == StorageTransactionType.TransshipmentReceipt);

        Assert.Equal(StorageTransactionsStatus.Confirmed, entry.TransactionStatus);
        Assert.Equal(DestinationWarehouse, entry.WarehouseCode);
        Assert.Equal(29_800m, entry.NetWeight);
        Assert.Null(entry.StorageAddressCode);

        var saved = await _db.Context.ShipmentLoadsTransshipments.AsNoTracking()
            .SingleAsync(x => x.Key == transshipment.Key);
        Assert.Equal(29_800m, saved.EntryQuantity);
        Assert.Equal(entry.Key, saved.EntryStorageTransactionKey);
    }

    [Fact]
    public async Task RegisterEntry_TransferToOwnWarehouse_EmitsTheReleaseAtTheDestinationWithoutLot()
    {
        var (_, transshipment, contract) = await SeedAsync();

        await RegisterService().ExecuteAsync(
            transshipment.Key!.Value, 29_800m, DateTime.Today, null, "tester");

        var release = await _db.Context.ShipmentReleases.AsNoTracking()
            .SingleAsync(x => x.Origin == ReleaseOrigin.Transshipment);

        Assert.Equal(contract.Key, release.PurchaseContractKey);
        Assert.Equal(DestinationWarehouse, release.DeliveryLocationCode);
        Assert.Equal(29_800m, release.ReleasedQuantity);
        Assert.Null(release.StorageAddressCode);
    }

    [Fact]
    public async Task RegisterEntry_TransferToOwnWarehouse_WithoutWeight_IsRefused()
    {
        var (_, transshipment, _) = await SeedAsync();
        // Receipt qualquer: no caminho de transbordo próprio ele seria aceito como entrada.
        var receipt = await RegisterOwnWarehouseEntryAsync(_db, transshipment);
        receipt.ShipmentLoadTransshipmentKey = null;
        transshipment.EntryQuantity = decimal.Zero;
        transshipment.EntryStorageTransactionKey = null;
        await _db.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<ApplicationException>(() => RegisterService().ExecuteAsync(
            transshipment.Key!.Value, 0m, DateTime.Today, receipt.Key, "tester"));

        Assert.Empty(await _db.Context.StorageTransactions.AsNoTracking()
            .Where(x => x.TransactionType == StorageTransactionType.TransshipmentReceipt)
            .ToListAsync());
        Assert.Null((await _db.Context.StorageTransactions.AsNoTracking()
            .SingleAsync(x => x.Key == receipt.Key)).ShipmentLoadTransshipmentKey);
    }

    [Fact]
    public async Task Complete_TransferToOwnWarehouse_ClosesTheLoadWithoutBilling()
    {
        var (load, transshipment, _) = await SeedAsync();
        await RegisterService().ExecuteAsync(
            transshipment.Key!.Value, 29_800m, DateTime.Today, null, "tester");

        var complete = new ShipmentLoadsTransshipmentCompleteService(
            _db, new ShipmentLoadsMovementLogService(_db.Context), new ShipmentLoadsChangeLogService(_db.Context));
        await complete.ExecuteAsync(transshipment.Key!.Value, "tester");

        var saved = await _db.Context.ShipmentLoadsTransshipments.AsNoTracking()
            .SingleAsync(x => x.Key == transshipment.Key);
        Assert.NotNull(saved.FinalDestinationAt);

        var savedLoad = await _db.Context.ShipmentLoads.AsNoTracking().SingleAsync(x => x.Key == load.Key);
        Assert.Equal(ShipmentLoadStatus.Completed, savedLoad.Status);
    }

    [Fact]
    public async Task Reverse_TransferToOwnWarehouse_CancelsTheCreditAndTheRelease()
    {
        var (_, transshipment, _) = await SeedAsync();
        await RegisterService().ExecuteAsync(
            transshipment.Key!.Value, 29_800m, DateTime.Today, null, "tester");

        await new ShipmentLoadsTransshipmentReverseService(_db, new ShipmentLoadsMovementLogService(_db.Context))
            .ExecuteAsync(transshipment.Key!.Value, null, "tester");

        var entry = await _db.Context.StorageTransactions.AsNoTracking()
            .SingleAsync(x => x.TransactionType == StorageTransactionType.TransshipmentReceipt);
        Assert.Equal(StorageTransactionsStatus.Cancelled, entry.TransactionStatus);

        var release = await _db.Context.ShipmentReleases.AsNoTracking()
            .SingleAsync(x => x.Origin == ReleaseOrigin.Transshipment);
        Assert.Equal(ReleaseStatus.Cancelled, release.Status);
    }
}
