using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.ShippingTransactions;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseContracts;

/// <summary>
/// A perna de compra (8) de uma Expedição de Grãos só se aloca no contrato da liberação dela, e a
/// alocação não se remove pela tela: a troca de contrato é a "Trocar liberação" da carga.
/// </summary>
public class ShippingPurchaseLegAllocationTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private ShipmentReleasesRecalculateShippedService Recalc() => new(_db.Context);

    private StorageTransactionsGetService GetService() =>
        new(_db, NullLogger<StorageTransactionsGetService>.Instance);

    private ShippingTransactionsCreateService CreateService()
    {
        var recalc = Recalc();
        var guard = new ShipmentReleaseMovementGuardService(_db.Context);
        var docNumbers = new FakeDocNumberSequenceService();

        var storageCreate = new StorageTransactionsCreateService(
            _db,
            docNumbers,
            new FakeBusinessPartnerService(new() { ["F0001"] = "Fornecedor" }),
            new FakeItemService(new() { ["SOJA"] = "SOJA EM GRAOS" }),
            new FakeWarehouseService(new() { ["01"] = "Armazém 01" }),
            recalc,
            guard,
            NullLogger<StorageTransactionsCreateService>.Instance);

        var storageConfirmed = new StorageTransactionsConfirmedService(
            _db,
            new FakeStringLocalizer<Resource>(),
            recalc,
            guard,
            NullLogger<StorageTransactionsConfirmedService>.Instance);

        var storageCopy = new StorageTransactionsCopyService(_db, docNumbers, storageCreate, new FakeStringLocalizer<Resource>());

        var allocationCreate = new PurchaseContractsAllocationCreateService(_db, GetService());

        return new ShippingTransactionsCreateService(
            _db, storageCreate, storageConfirmed, storageCopy, allocationCreate, recalc,
            new FakeStorageAddressBalanceReader(100_000m));
    }

    private ShippingTransactionsReverseService ReverseService() =>
        new(_db,
            GetService(),
            new PurchaseContractsAllocationDeleteService(
                _db, NullLogger<PurchaseContractsAllocationDeleteService>.Instance),
            Recalc(),
            NullLogger<ShippingTransactionsReverseService>.Instance);

    /// <summary>Contrato aprovado com uma liberação ativa e saldo de sobra.</summary>
    private async Task<(PurchaseContract Contract, ShipmentRelease Release)> SeedAsync(
        decimal totalVolume = 10000m,
        decimal releasedQuantity = 1500m,
        ReleaseOrigin origin = ReleaseOrigin.Standard,
        string? lotCode = null)
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
            TotalVolume = totalVolume,
            AllocatedVolume = 0m,
        };

        var release = new ShipmentRelease
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = contract.Key,
            DeliveryLocationCode = "01",
            ReleasedQuantity = releasedQuantity,
            ShippedQuantity = 0m,
            Status = ReleaseStatus.Actived,
            Origin = origin,
            StorageAddressCode = lotCode,
        };

        if (lotCode != null)
        {
            _db.Context.StorageAddresses.Add(new StorageAddress
            {
                Code = lotCode,
                Description = "Lote próprio",
                CardCode = "E0001",
                ItemCode = "SOJA",
                WarehouseCode = "01",
                UoM = "KG",
                OwnershipType = StorageOwnershipType.OwnedInOurCustody,
                Status = StorageAddressStatus.Open,
            });
        }

        _db.Context.PurchaseContracts.Add(contract);
        _db.Context.ShipmentReleases.Add(release);
        await _db.Context.SaveChangesAsync();

        return (contract, release);
    }

    private static StorageTransaction NewPurchase(Guid? releaseKey, decimal grossWeight) => new()
    {
        Key = Guid.NewGuid(),
        CardCode = "F0001",
        ItemCode = "SOJA",
        UnitOfMeasureCode = "KG",
        WarehouseCode = "01",
        TransactionType = StorageTransactionType.Purchase,
        TransactionStatus = StorageTransactionsStatus.Pending,
        GrossWeight = grossWeight,
        ShipmentReleaseKey = releaseKey,
    };

    private PurchaseContractsAllocationCreateService AllocCreate() => new(_db, GetService());

    private PurchaseContractsAllocationDeleteService AllocDelete() =>
        new(_db, NullLogger<PurchaseContractsAllocationDeleteService>.Instance);

    private async Task<PurchaseContract> SeedOtherContractAsync(string cardCode = "F0001")
    {
        var other = new PurchaseContract
        {
            Key = Guid.NewGuid(), Code = "PC-002", CardCode = cardCode, ItemCode = "SOJA",
            UnitOfMeasureCode = "KG", HarvestSeasonCode = "2026", DeliveryLocationCode = "01",
            Status = ContractStatus.Approved, TotalVolume = 10000m,
        };
        _db.Context.PurchaseContracts.Add(other);
        await _db.Context.SaveChangesAsync();
        return other;
    }

    /// <summary>Expedição criada e com a alocação da perna de compra desfeita à mão (como na CEAGUI).</summary>
    private async Task<(PurchaseContract Contract, Guid PurchaseKey)> SeedDeallocatedShippingAsync()
    {
        var (contract, release) = await SeedAsync();
        var shipping = await CreateService().ExecuteAsync(contract.Key, NewPurchase(release.Key, 1000m), "tester");
        var purchaseKey = shipping.PurchaseStorageTransactionKey!.Value;
        var allocKey = (await _db.Context.PurchaseContractsAllocations.AsNoTracking()
            .SingleAsync(x => x.StorageTransactionKey == purchaseKey)).Key;
        await AllocDelete().ExecuteAsync(allocKey, "tester");
        return (contract, purchaseKey);
    }

    [Fact]
    public async Task ShippingLeg_AllocateInReleaseContract_Works()
    {
        var (contract, purchaseKey) = await SeedDeallocatedShippingAsync();

        await AllocCreate().ExecuteWithTransactionAsync(contract.Key, purchaseKey, 1000m, "tester");

        var alloc = await _db.Context.PurchaseContractsAllocations.AsNoTracking()
            .SingleAsync(x => x.StorageTransactionKey == purchaseKey);
        Assert.Equal(contract.Key, alloc.PurchaseContractKey);
    }

    [Fact]
    public async Task ShippingLeg_AllocateInOtherContract_IsRefused()
    {
        var (_, purchaseKey) = await SeedDeallocatedShippingAsync();
        var other = await SeedOtherContractAsync();

        var error = await Assert.ThrowsAsync<DefaultException>(
            () => AllocCreate().ExecuteWithTransactionAsync(other.Key, purchaseKey, 1000m, "tester"));

        Assert.Equal(
            "Romaneio de Expedição só pode ser alocado no contrato da liberação (PC-001). " +
            "Para mudar o contrato, use Trocar liberação na carga.", error.Message);
        Assert.False(await _db.Context.PurchaseContractsAllocations.AsNoTracking()
            .AnyAsync(x => x.StorageTransactionKey == purchaseKey));
    }

    [Fact]
    public async Task ShippingLeg_DeleteAllocation_IsRefused()
    {
        var (contract, release) = await SeedAsync();
        var shipping = await CreateService().ExecuteAsync(contract.Key, NewPurchase(release.Key, 1000m), "tester");
        var purchaseKey = shipping.PurchaseStorageTransactionKey!.Value;
        var allocKey = (await _db.Context.PurchaseContractsAllocations.AsNoTracking()
            .SingleAsync(x => x.StorageTransactionKey == purchaseKey)).Key;

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => AllocDelete().ExecuteWithTransactionAsync(allocKey, "tester"));

        Assert.Equal(
            "Esta alocação pertence a uma Expedição de Grãos. Use Trocar liberação na carga.", error.Message);
        Assert.True(await _db.Context.PurchaseContractsAllocations.AsNoTracking()
            .AnyAsync(x => x.Key == allocKey));
    }

    [Fact]
    public async Task ShippingReverse_StillRemovesLegAllocation()
    {
        var (contract, release) = await SeedAsync();
        var shipping = await CreateService().ExecuteAsync(contract.Key, NewPurchase(release.Key, 1000m), "tester");
        var purchaseKey = shipping.PurchaseStorageTransactionKey!.Value;

        await ReverseService().ExecuteAsync(shipping.SalesStorageTransactionKey, "tester");

        Assert.False(await _db.Context.PurchaseContractsAllocations.AsNoTracking()
            .AnyAsync(x => x.StorageTransactionKey == purchaseKey));
    }

    [Fact]
    public async Task StandalonePurchase_NotAffected()
    {
        var (contract, _) = await SeedAsync();
        var purchase = NewPurchase(null, 1000m);
        purchase.NetWeight = 1000m;
        purchase.TransactionStatus = StorageTransactionsStatus.Confirmed;
        purchase.AvaiableVolumeToAllocate = 1000m;
        _db.Context.StorageTransactions.Add(purchase);
        await _db.Context.SaveChangesAsync();

        await AllocCreate().ExecuteWithTransactionAsync(contract.Key, purchase.Key, 1000m, "tester");
        var allocKey = (await _db.Context.PurchaseContractsAllocations.AsNoTracking()
            .SingleAsync(x => x.StorageTransactionKey == purchase.Key)).Key;
        await AllocDelete().ExecuteWithTransactionAsync(allocKey, "tester");

        Assert.False(await _db.Context.PurchaseContractsAllocations.AsNoTracking()
            .AnyAsync(x => x.StorageTransactionKey == purchase.Key));
    }
}
