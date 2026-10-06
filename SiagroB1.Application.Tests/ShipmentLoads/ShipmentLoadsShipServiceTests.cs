using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.ShippingTransactions;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>"Expedir" no detalhe da carga: o romaneio nasce vinculado, numa transação só.</summary>
public class ShipmentLoadsShipServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private (ShipmentLoadsShipService Service, CountingUnitOfWork Counting) Build(
        ShipmentLoadsAttachTransactionsService? attachOverride = null)
    {
        var counting = new CountingUnitOfWork(_db);
        var recalc = new ShipmentReleasesRecalculateShippedService(_db.Context);
        var guard = new ShipmentReleaseMovementGuardService(_db.Context);
        var docNumbers = new FakeDocNumberSequenceService();
        var storageCreate = new StorageTransactionsCreateService(
            _db, docNumbers, new FakeBusinessPartnerService(new() { ["F0001"] = "Fornecedor" }),
            new FakeItemService(new() { ["SOJA"] = "SOJA EM GRAOS" }), new FakeWarehouseService(new() { ["01"] = "Armazém 01" }),
            recalc, guard, NullLogger<StorageTransactionsCreateService>.Instance);
        var storageConfirmed = new StorageTransactionsConfirmedService(
            _db, new FakeStringLocalizer<Resource>(), recalc, guard, NullLogger<StorageTransactionsConfirmedService>.Instance);
        var storageCopy = new StorageTransactionsCopyService(_db, docNumbers, storageCreate, new FakeStringLocalizer<Resource>());
        var allocation = new PurchaseContractsAllocationCreateService(
            _db, new StorageTransactionsGetService(_db, NullLogger<StorageTransactionsGetService>.Instance));
        var shipping = new ShippingTransactionsCreateService(
            _db, storageCreate, storageConfirmed, storageCopy, allocation, recalc, new FakeStorageAddressBalanceReader(100_000m));
        var attach = attachOverride ?? new ShipmentLoadsAttachTransactionsService(_db, new ShipmentLoadsMovementLogService(_db.Context));

        return (new ShipmentLoadsShipService(counting, shipping, attach, recalc), counting);
    }

    private async Task<(ShipmentLoad Load, PurchaseContract Contract, ShipmentRelease Release)> SeedAsync(
        ShipmentLoadStatus status = ShipmentLoadStatus.Planned, ShipmentLoadType type = ShipmentLoadType.Normal,
        string? truckCode = "ABC1D23", string itemCode = "SOJA")
    {
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(), Code = "PC-001", CardCode = "F0001", CardName = "Fornecedor", ItemCode = itemCode,
            UnitOfMeasureCode = "KG", HarvestSeasonCode = "2026", DeliveryLocationCode = "01",
            Status = ContractStatus.Approved, TotalVolume = 10_000m, AllocatedVolume = 0m,
        };
        var release = new ShipmentRelease
        {
            Key = Guid.NewGuid(), PurchaseContractKey = contract.Key, DeliveryLocationCode = "01",
            ReleasedQuantity = 5_000m, ShippedQuantity = 0m, Status = ReleaseStatus.Actived,
        };
        var load = new ShipmentLoad
        {
            Key = Guid.NewGuid(), Code = "CG000001", BranchCode = "01", ItemCode = "SOJA", ItemName = "SOJA EM GRAOS",
            UnitOfMeasureCode = "KG", TruckCode = truckCode, WarehouseCode = "01", Status = status, LoadType = type,
        };

        _db.Context.PurchaseContracts.Add(contract);
        _db.Context.ShipmentReleases.Add(release);
        _db.Context.ShipmentLoads.Add(load);
        await _db.SaveChangesAsync();
        return (load, contract, release);
    }

    private static ShipmentLoadShipRequest Request(ShipmentLoad load, ShipmentRelease release, decimal weight = 1_000m) =>
        new(load.Key, release.Key, "01", "MOT01", new DateTime(2026, 10, 6), weight, "teste");

    [Fact]
    public async Task Ships_the_load_in_one_transaction()
    {
        var (load, contract, release) = await SeedAsync();
        var (service, counting) = Build();

        var result = await service.ExecuteAsync(Request(load, release), "tester");

        var exit = await _db.Context.StorageTransactions.AsNoTracking().SingleAsync(x => x.Key == result.StorageTransactionKey);
        Assert.Equal(StorageTransactionType.SalesShipment, exit.TransactionType);
        Assert.Equal(load.Key, exit.ShipmentLoadKey);
        Assert.Equal("ABC1D23", exit.TruckCode);
        Assert.Equal("01", exit.BranchCode);
        Assert.Equal(1_000m, (await _db.Context.ShipmentLoads.AsNoTracking().SingleAsync(x => x.Key == load.Key)).TotalQuantity);
        Assert.Equal(1_000m, (await _db.Context.PurchaseContracts.AsNoTracking().SingleAsync(x => x.Key == contract.Key)).AllocatedVolume);
        Assert.Equal(1, counting.Begins);
        Assert.Equal(1, counting.Commits);
        Assert.Equal(0, counting.Rollbacks);
    }

    [Fact]
    public async Task Release_balance_is_recalculated_after_the_commit()
    {
        var (load, _, release) = await SeedAsync();

        await Build().Service.ExecuteAsync(Request(load, release), "tester");

        Assert.Equal(1_000m, (await _db.Context.ShipmentReleases.AsNoTracking().SingleAsync(x => x.Key == release.Key)).ShippedQuantity);
    }

    [Theory]
    [InlineData(ShipmentLoadStatus.Cancelled, ShipmentLoadType.Normal)]
    [InlineData(ShipmentLoadStatus.Invoiced, ShipmentLoadType.Normal)]
    [InlineData(ShipmentLoadStatus.Planned, ShipmentLoadType.Removal)]
    public async Task Load_that_does_not_accept_shipments_is_refused_before_writing(ShipmentLoadStatus status, ShipmentLoadType type)
    {
        var (load, _, release) = await SeedAsync(status, type);
        var (service, counting) = Build();

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => service.ExecuteAsync(Request(load, release), "tester"));

        Assert.Equal("A carga CG000001 não aceita expedição nesta situação.", ex.Message);
        Assert.Equal(0, counting.Begins);
        Assert.Empty(await _db.Context.StorageTransactions.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Load_without_truck_is_refused()
    {
        var (load, _, release) = await SeedAsync(truckCode: null);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => Build().Service.ExecuteAsync(Request(load, release), "tester"));

        Assert.Equal("A carga CG000001 não tem placa: informe a placa na carga antes de expedir.", ex.Message);
    }

    [Fact]
    public async Task Release_of_another_item_is_refused()
    {
        var (load, _, release) = await SeedAsync(itemCode: "MILHO");

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => Build().Service.ExecuteAsync(Request(load, release), "tester"));

        Assert.Equal("A liberação escolhida é de outro produto.", ex.Message);
    }

    [Fact]
    public async Task Zero_weight_is_refused()
    {
        var (load, _, release) = await SeedAsync();

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => Build().Service.ExecuteAsync(Request(load, release, 0m), "tester"));

        Assert.Equal("Informe o peso bruto maior que zero.", ex.Message);
    }

    /// <summary>
    /// Falha no vínculo depois da expedição: a transação aberta pelo serviço é desfeita, nunca confirmada. O InMemory
    /// não desfaz o que já foi salvo, então o teste prova a ORDEM; o desfazer real é conferido no SQL Server (Task 5).
    /// </summary>
    [Fact]
    public async Task Attach_failure_rolls_back_the_whole_transaction()
    {
        var (load, _, release) = await SeedAsync();
        var (service, counting) = Build(new ThrowingAttach(_db));

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => service.ExecuteAsync(Request(load, release), "tester"));

        Assert.Equal("vínculo recusado", ex.Message);
        Assert.Equal(1, counting.Begins);
        Assert.Equal(0, counting.Commits);
        Assert.Equal(1, counting.Rollbacks);
    }

    private sealed class ThrowingAttach(UnitOfWork db)
        : ShipmentLoadsAttachTransactionsService(db, new ShipmentLoadsMovementLogService(db.Context))
    {
        public override Task<ShipmentLoad> ExecuteAsync(
            Guid shipmentLoadKey, ICollection<Guid> storageTransactionKeys, Guid? transshipmentKey, string userName,
            CommitMode commitMode = CommitMode.Auto) =>
            throw new ApplicationException("vínculo recusado");
    }
}
