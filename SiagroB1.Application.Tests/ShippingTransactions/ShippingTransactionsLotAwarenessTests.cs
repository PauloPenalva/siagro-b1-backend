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
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShippingTransactions;

/// <summary>
/// A Expedição de Grãos opera em nível de armazém. Quando a liberação vem de uma
/// transferência de titularidade, o grão já está num lote próprio — e a saída
/// precisa drenar aquele lote, senão o Receipt(0) gravado pela transferência fica
/// como saldo fantasma permanente.
/// </summary>
public class ShippingTransactionsLotAwarenessTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private ShippingTransactionsCreateService CreateService(decimal lotBalance = 100_000m)
    {
        var recalc = new ShipmentReleasesRecalculateShippedService(_db.Context);
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

        return new ShippingTransactionsCreateService(
            _db,
            storageCreate,
            storageConfirmed,
            new StorageTransactionsCopyService(_db, docNumbers, storageCreate),
            new PurchaseContractsAllocationCreateService(
                _db,
                new StorageTransactionsGetService(
                    _db, NullLogger<StorageTransactionsGetService>.Instance)),
            recalc,
            new FakeStorageAddressBalanceReader(lotBalance));
    }

    private async Task<(PurchaseContract Contract, ShipmentRelease Release)> SeedAsync(
        string? lotCode,
        ReleaseOrigin origin = ReleaseOrigin.Standard,
        string lotItemCode = "SOJA")
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

        if (lotCode != null)
        {
            _db.Context.StorageAddresses.Add(new StorageAddress
            {
                Code = lotCode,
                Description = "Lote próprio",
                CardCode = "E0001",
                ItemCode = lotItemCode,
                WarehouseCode = "01",
                UoM = "KG",
                OwnershipType = StorageOwnershipType.OwnedInOurCustody,
                Status = StorageAddressStatus.Open,
            });
        }

        var release = new ShipmentRelease
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = contract.Key,
            DeliveryLocationCode = "01",
            ReleasedQuantity = 1500m,
            ShippedQuantity = 0m,
            Status = ReleaseStatus.Actived,
            Origin = origin,
            StorageAddressCode = lotCode,
        };

        _db.Context.PurchaseContracts.Add(contract);
        _db.Context.ShipmentReleases.Add(release);
        await _db.Context.SaveChangesAsync();

        return (contract, release);
    }

    private static StorageTransaction NewPurchase(Guid releaseKey, decimal grossWeight) => new()
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

    /// <summary>
    /// Embarque de liberação de transferência: a compra já foi registrada no confirm da
    /// transferência, então a Expedição cria SÓ a saída — carimbada com o lote, para
    /// drenar o saldo que o Receipt(0) da transferência deixou lá.
    /// </summary>
    [Fact]
    public async Task Execute_OwnershipTransferRelease_CreatesOnlyTheSalesShipmentLeg()
    {
        var (contract, release) = await SeedAsync(
            "LOTE-DEST", ReleaseOrigin.OwnershipTransfer);

        var shipping = await CreateService()
            .ExecuteAsync(contract.Key, NewPurchase(release.Key, 1000m), "tester");

        var legs = await _db.Context.StorageTransactions
            .AsNoTracking().Where(x => x.ShipmentReleaseKey == release.Key).ToListAsync();

        var sales = Assert.Single(legs);
        Assert.Equal(StorageTransactionType.SalesShipment, sales.TransactionType);
        Assert.Equal("LOTE-DEST", sales.StorageAddressCode);
        Assert.Null(shipping.PurchaseStorageTransactionKey);
    }

    /// <summary>
    /// O contrato pertence à transferência: um segundo débito pelo mesmo grão dobraria o
    /// consumo. Já era assim antes do desenho 2 (o guard só pulava a alocação); aqui o
    /// Purchase(8) sequer existe.
    /// </summary>
    [Fact]
    public async Task Execute_OwnershipTransferRelease_DoesNotAllocateTheContract()
    {
        var (contract, release) = await SeedAsync(
            "LOTE-DEST", ReleaseOrigin.OwnershipTransfer);

        await CreateService().ExecuteAsync(contract.Key, NewPurchase(release.Key, 1000m), "tester");

        Assert.Empty(await _db.Context.PurchaseContractsAllocations.AsNoTracking().ToListAsync());
    }

    /// <summary>
    /// A saída é o único romaneio do embarque de transferência, logo é ela quem consome a
    /// liberação — a fórmula por origem em <c>CalculateShippedAsync</c> é o que sustenta isso.
    /// </summary>
    [Fact]
    public async Task Execute_OwnershipTransferRelease_ConsumesTheReleaseThroughTheSalesLeg()
    {
        var (contract, release) = await SeedAsync(
            "LOTE-DEST", ReleaseOrigin.OwnershipTransfer);

        await CreateService().ExecuteAsync(contract.Key, NewPurchase(release.Key, 1000m), "tester");

        var reloaded = await _db.Context.ShipmentReleases
            .AsNoTracking().SingleAsync(x => x.Key == release.Key);
        Assert.Equal(1000m, reloaded.ShippedQuantity);
        Assert.Equal(500m, reloaded.AvailableQuantity); // 1500 liberados − 1000 embarcados
    }

    /// <summary>
    /// O armazém vem do payload da tela e é ele que o saldo de armazém debita. Divergindo
    /// do armazém do lote, a entrada da transferência ficaria presa num armazém e a saída
    /// debitaria outro.
    /// </summary>
    [Fact]
    public async Task Execute_BlocksWhenTheShipmentWarehouseDiffersFromTheLotWarehouse()
    {
        var (contract, release) = await SeedAsync(
            "LOTE-DEST", ReleaseOrigin.OwnershipTransfer);

        var purchase = NewPurchase(release.Key, 1000m);
        purchase.WarehouseCode = "02";

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => CreateService().ExecuteAsync(contract.Key, purchase, "tester"));

        Assert.Contains("armazém do embarque", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await _db.Context.StorageTransactions.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Execute_LeavesBothLegsWithoutLotForAStandardRelease()
    {
        var (contract, release) = await SeedAsync(lotCode: null);

        await CreateService().ExecuteAsync(contract.Key, NewPurchase(release.Key, 1000m), "tester");

        var pair = await _db.Context.StorageTransactions
            .AsNoTracking().Where(x => x.ShipmentReleaseKey == release.Key).ToListAsync();

        Assert.Equal(2, pair.Count);
        Assert.All(pair, x => Assert.Null(x.StorageAddressCode));
    }

    [Fact]
    public async Task Execute_BlocksWhenTheLotBalanceIsInsufficient()
    {
        var (contract, release) = await SeedAsync(
            "LOTE-DEST", ReleaseOrigin.OwnershipTransfer);

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => CreateService(lotBalance: 400m)
                .ExecuteAsync(contract.Key, NewPurchase(release.Key, 1000m), "tester"));

        Assert.Contains("Saldo insuficiente", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await _db.Context.StorageTransactions.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Execute_BlocksWhenTheLotItemDiffers()
    {
        var (contract, release) = await SeedAsync(
            "LOTE-DEST", ReleaseOrigin.OwnershipTransfer, lotItemCode: "MILHO");

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => CreateService().ExecuteAsync(contract.Key, NewPurchase(release.Key, 1000m), "tester"));

        Assert.Contains("produto do lote", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Execute_BlocksAnOwnershipTransferReleaseWithoutLot()
    {
        var (contract, release) = await SeedAsync(
            lotCode: null, origin: ReleaseOrigin.OwnershipTransfer);

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => CreateService().ExecuteAsync(contract.Key, NewPurchase(release.Key, 1000m), "tester"));

        Assert.Contains("sem lote", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
