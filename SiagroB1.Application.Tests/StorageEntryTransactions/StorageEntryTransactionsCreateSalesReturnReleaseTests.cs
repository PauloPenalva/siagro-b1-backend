using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.StorageEntryTransactions;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.StorageEntryTransactions;

/// <summary>
/// GAC-1214: Entrada em Armazenagem a partir de uma liberação de devolução ao armazém
/// (<see cref="ReleaseOrigin.SalesReturn"/>). O grão já está no armazém de terceiro, creditado
/// pelo romaneio tipo 12, e o contrato já foi debitado na primeira saída. A entrada tem de
/// criar Saída(7) + Recebimento(0) — como a Expedição de Grãos — e não Compra(8) + alocação,
/// que baixava o contrato de novo, creditava o armazém de novo e não consumia a liberação.
/// </summary>
public class StorageEntryTransactionsCreateSalesReturnReleaseTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private StorageEntryTransactionsCreateService CreateService()
    {
        var recalc = new ShipmentReleasesRecalculateShippedService(_db.Context);
        var guard = new ShipmentReleaseMovementGuardService(_db.Context);

        var storageCreate = new StorageTransactionsCreateService(
            _db,
            new FakeDocNumberSequenceService(),
            new FakeBusinessPartnerService(new() { ["F0001"] = "Fornecedor", ["C0001"] = "Empresa" }),
            new FakeItemService(new() { ["SOJA"] = "SOJA EM GRAOS" }),
            new FakeWarehouseService(new() { ["01"] = "Armazém terceiro", ["02"] = "Armazém próprio" }),
            recalc,
            guard,
            NullLogger<StorageTransactionsCreateService>.Instance);

        var storageConfirmed = new StorageTransactionsConfirmedService(
            _db,
            new FakeStringLocalizer<Resource>(),
            recalc,
            guard,
            NullLogger<StorageTransactionsConfirmedService>.Instance);

        return new StorageEntryTransactionsCreateService(
            _db,
            storageCreate,
            storageConfirmed,
            new PurchaseContractsAllocationCreateService(
                _db,
                new StorageTransactionsGetService(_db, NullLogger<StorageTransactionsGetService>.Instance)),
            recalc);
    }

    private async Task<(PurchaseContract Contract, ShipmentRelease Release)> SeedAsync(
        ReleaseOrigin origin = ReleaseOrigin.SalesReturn, decimal quantity = 1500m)
    {
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(),
            Code = "PC-001",
            CardCode = "F0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "2026",
            DeliveryLocationCode = "01",
            Status = ContractStatus.Approved,
            TotalVolume = 10_000m,
            AllocatedVolume = 0m,
        };

        var returned = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = "DEV-001",
            CardCode = "C0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            WarehouseCode = "01",
            TransactionType = StorageTransactionType.SalesShipmentReturn,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
            GrossWeight = quantity,
            NetWeight = quantity,
        };

        var release = new ShipmentRelease
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = contract.Key,
            DeliveryLocationCode = "01",
            ReleasedQuantity = quantity,
            ShippedQuantity = 0m,
            Status = ReleaseStatus.Actived,
            Origin = origin,
            GeneratedByStorageTransactionKey = returned.Key,
        };

        var lot = new StorageAddress
        {
            Code = "LOTE-01",
            Description = "Lote próprio",
            CardCode = "C0001",
            ItemCode = "SOJA",
            WarehouseCode = "02",
            UoM = "KG",
            Status = StorageAddressStatus.Open,
        };

        _db.Context.PurchaseContracts.Add(contract);
        _db.Context.StorageTransactions.Add(returned);
        _db.Context.ShipmentReleases.Add(release);
        _db.Context.StorageAddresses.Add(lot);
        await _db.Context.SaveChangesAsync();

        // GAC-1214: a devolução já devolveu o saldo ao contrato (alocação −Q no romaneio gerador).
        if (origin == ReleaseOrigin.SalesReturn)
            await PurchaseContractsReturnReleaseAllocationSync.SyncAsync(_db.Context, release.Key);

        return (contract, release);
    }

    private static StorageTransaction Payload(Guid releaseKey, decimal weight) => new()
    {
        Key = Guid.NewGuid(),
        CardCode = "F0001",
        ItemCode = "SOJA",
        UnitOfMeasureCode = "KG",
        WarehouseCode = "01",
        TransactionType = StorageTransactionType.Purchase,
        TransactionStatus = StorageTransactionsStatus.Pending,
        GrossWeight = weight,
        NetWeight = weight,
        ShipmentReleaseKey = releaseKey,
    };

    [Fact]
    public async Task Execute_SalesReturnRelease_CreatesSalesShipmentLegInsteadOfPurchase()
    {
        var (contract, release) = await SeedAsync();

        var entry = await CreateService().ExecuteAsync(contract.Key, "LOTE-01", Payload(release.Key, 1500m), "tester");

        var leg = await _db.Context.StorageTransactions.AsNoTracking()
            .SingleAsync(x => x.Key == entry.PurchaseStorageTransactionKey);
        Assert.Equal(StorageTransactionType.SalesShipment, leg.TransactionType);
        Assert.Equal(StorageTransactionsStatus.Confirmed, leg.TransactionStatus);
        Assert.Equal(0m, entry.AllocatedVolume);
    }

    [Fact]
    public async Task Execute_SalesReturnRelease_ConsumesTheContractAgain()
    {
        var (contract, release) = await SeedAsync();
        Assert.Equal(-1500m,
            (await _db.Context.PurchaseContracts.AsNoTracking().SingleAsync(x => x.Key == contract.Key)).AllocatedVolume);

        var entry = await CreateService().ExecuteAsync(contract.Key, "LOTE-01", Payload(release.Key, 1500m), "tester");

        var allocation = await _db.Context.PurchaseContractsAllocations.AsNoTracking()
            .SingleAsync(x => x.StorageTransactionKey == entry.PurchaseStorageTransactionKey);
        Assert.Equal(1500m, allocation.Volume);
        var reloaded = await _db.Context.PurchaseContracts.AsNoTracking().SingleAsync(x => x.Key == contract.Key);
        Assert.Equal(0m, reloaded.AllocatedVolume); // −1 500 da devolução + 1 500 da saída
        Assert.Equal(0m, entry.AllocatedVolume); // o campo é da perna de compra
    }

    [Fact]
    public async Task Execute_SalesReturnRelease_ConsumesTheRelease()
    {
        var (contract, release) = await SeedAsync();

        await CreateService().ExecuteAsync(contract.Key, "LOTE-01", Payload(release.Key, 1500m), "tester");

        var reloaded = await _db.Context.ShipmentReleases.AsNoTracking().SingleAsync(x => x.Key == release.Key);
        Assert.Equal(1500m, reloaded.ShippedQuantity);
    }

    [Fact]
    public async Task Execute_SalesReturnRelease_DrainsTheThirdPartyWarehouse()
    {
        var (contract, release) = await SeedAsync();

        await CreateService().ExecuteAsync(contract.Key, "LOTE-01", Payload(release.Key, 1500m), "tester");

        var balance = await StorageTransactionsWarehouseBalanceService.CalculateAsync(_db.Context, "01", "SOJA");
        Assert.Equal(0m, balance); // +1500 (tipo 12) − 1500 (tipo 7)
    }

    [Fact]
    public async Task Execute_StandardRelease_StillCreatesPurchaseAndAllocates()
    {
        var (contract, release) = await SeedAsync(ReleaseOrigin.Standard);

        var entry = await CreateService().ExecuteAsync(contract.Key, "LOTE-01", Payload(release.Key, 1500m), "tester");

        var leg = await _db.Context.StorageTransactions.AsNoTracking()
            .SingleAsync(x => x.Key == entry.PurchaseStorageTransactionKey);
        Assert.Equal(StorageTransactionType.Purchase, leg.TransactionType);
        Assert.Single(await _db.Context.PurchaseContractsAllocations.AsNoTracking().ToListAsync());
    }

    private StorageEntryTransactionsCancelService CreateCancelService() => new(
        _db,
        new PurchaseContractsAllocationDeleteService(
            _db, NullLogger<PurchaseContractsAllocationDeleteService>.Instance),
        new StorageTransactionsCancelService(
            _db, new ShipmentReleasesRecalculateShippedService(_db.Context)),
        new FakeStorageAddressBalanceReader(100_000m));

    [Fact]
    public async Task Cancel_SalesReturnEntry_GivesTheContractBalanceBackToo()
    {
        var (contract, release) = await SeedAsync();
        var entry = await CreateService().ExecuteAsync(contract.Key, "LOTE-01", Payload(release.Key, 1500m), "tester");

        await CreateCancelService().ExecuteAsync(entry.Key, "tester");

        var allocation = Assert.Single(await _db.Context.PurchaseContractsAllocations.AsNoTracking().ToListAsync());
        Assert.NotEqual(entry.PurchaseStorageTransactionKey, allocation.StorageTransactionKey);
        Assert.Equal(-1500m, allocation.Volume); // só a da devolução
        var reloaded = await _db.Context.PurchaseContracts.AsNoTracking().SingleAsync(x => x.Key == contract.Key);
        Assert.Equal(-1500m, reloaded.AllocatedVolume);
    }

    [Fact]
    public async Task Cancel_SalesReturnEntry_OnFinishedContract_IsAllowed()
    {
        var (contract, release) = await SeedAsync();
        var entry = await CreateService().ExecuteAsync(contract.Key, "LOTE-01", Payload(release.Key, 1500m), "tester");
        var tracked = await _db.Context.PurchaseContracts.SingleAsync(x => x.Key == contract.Key);
        tracked.Status = ContractStatus.Finished;
        await _db.Context.SaveChangesAsync();

        await CreateCancelService().ExecuteAsync(entry.Key, "tester");

        var reloaded = await _db.Context.StorageEntryTransactions.AsNoTracking().SingleAsync(x => x.Key == entry.Key);
        Assert.Equal(StorageEntryTransactionStatus.Cancelled, reloaded.Status);
        Assert.Equal(-1500m,
            (await _db.Context.PurchaseContracts.AsNoTracking().SingleAsync(x => x.Key == contract.Key)).AllocatedVolume);
    }

    /// <summary>Só a devolução ao armazém ganha o bypass: as outras origens seguem barradas por contrato encerrado.</summary>
    [Fact]
    public async Task Cancel_TransshipmentEntry_OnFinishedContract_StillThrows()
    {
        var (contract, release) = await SeedAsync(ReleaseOrigin.Transshipment);
        var entry = await CreateService().ExecuteAsync(contract.Key, "LOTE-01", Payload(release.Key, 1500m), "tester");
        var tracked = await _db.Context.PurchaseContracts.SingleAsync(x => x.Key == contract.Key);
        tracked.Status = ContractStatus.Finished;
        await _db.Context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => CreateCancelService().ExecuteAsync(entry.Key, "tester"));

        Assert.Equal("Contrato encerrado: não é possível estornar a entrada em armazenagem.", ex.Message);
    }

    [Fact]
    public async Task Cancel_SalesReturnEntry_GivesTheReleaseAndWarehouseBack()
    {
        var (contract, release) = await SeedAsync();
        var entry = await CreateService().ExecuteAsync(contract.Key, "LOTE-01", Payload(release.Key, 1500m), "tester");

        await CreateCancelService().ExecuteAsync(entry.Key, "tester");

        var reloaded = await _db.Context.ShipmentReleases.AsNoTracking().SingleAsync(x => x.Key == release.Key);
        Assert.Equal(0m, reloaded.ShippedQuantity);
        Assert.Equal(1500m,
            await StorageTransactionsWarehouseBalanceService.CalculateAsync(_db.Context, "01", "SOJA"));
    }
}
