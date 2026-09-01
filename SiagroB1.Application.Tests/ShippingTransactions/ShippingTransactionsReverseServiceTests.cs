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
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShippingTransactions;

/// <summary>
/// Estorno do romaneio de embarque (tela /shipment-loads, Montagem de Carga). Vive ao lado do
/// <c>ShippingTransactionsCreateService</c>, que é o que ele desfaz. Cancela o par
/// Purchase/SalesShipment escrevendo o status direto — não passa por
/// <c>StorageTransactionsCancelService</c>, que é onde vive o hook de <c>ShippedQuantity</c>;
/// sem um recálculo explícito o saldo da liberação de embarque fica preso no valor anterior.
/// </summary>
public class ShippingTransactionsReverseServiceTests
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

        var storageCopy = new StorageTransactionsCopyService(_db, docNumbers, storageCreate);

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

    [Fact]
    public async Task Execute_DevolveOSaldoDaLiberacaoDeEmbarque()
    {
        var (contract, release) = await SeedAsync();
        var purchase = NewPurchase(release.Key, 1000m);
        var shipping = await CreateService().ExecuteAsync(contract.Key, purchase, "tester");

        await ReverseService().ExecuteAsync(shipping.SalesStorageTransactionKey, "tester");

        var reloaded = await _db.Context.ShipmentReleases
            .AsNoTracking().SingleAsync(x => x.Key == release.Key);

        Assert.Equal(0m, reloaded.ShippedQuantity);
        Assert.Equal(1500m, reloaded.AvailableQuantity);
    }

    /// <summary>
    /// O estorno já devolvia o saldo do contrato (cascata da alocação); o teste trava a
    /// regressão junto com o saldo da liberação.
    /// </summary>
    [Fact]
    public async Task Execute_DevolveOSaldoAlocadoDoContrato()
    {
        var (contract, release) = await SeedAsync();
        var purchase = NewPurchase(release.Key, 1000m);
        var shipping = await CreateService().ExecuteAsync(contract.Key, purchase, "tester");

        await ReverseService().ExecuteAsync(shipping.SalesStorageTransactionKey, "tester");

        var reloaded = await _db.Context.PurchaseContracts
            .AsNoTracking().SingleAsync(x => x.Key == contract.Key);

        Assert.Equal(0m, reloaded.AllocatedVolume);
    }

    /// <summary>
    /// Estorno parcial: com dois embarques na mesma liberação, estornar um devolve
    /// apenas o volume dele. O recálculo deriva da soma dos romaneios vivos, não de um
    /// decremento — estornar de novo não pode zerar o que ainda está embarcado.
    /// </summary>
    [Fact]
    public async Task Execute_ComDoisEmbarques_DevolveApenasOEstornado()
    {
        var (contract, release) = await SeedAsync();

        var first = await CreateService()
            .ExecuteAsync(contract.Key, NewPurchase(release.Key, 1000m), "tester");
        await CreateService()
            .ExecuteAsync(contract.Key, NewPurchase(release.Key, 400m), "tester");

        await ReverseService().ExecuteAsync(first.SalesStorageTransactionKey, "tester");

        var reloaded = await _db.Context.ShipmentReleases
            .AsNoTracking().SingleAsync(x => x.Key == release.Key);

        Assert.Equal(400m, reloaded.ShippedQuantity);
        Assert.Equal(1100m, reloaded.AvailableQuantity);
    }

    /// <summary>
    /// Romaneio montado em carga não é estornável: o estorno cancela o par e devolve os saldos
    /// à origem, o que arrancaria volume de baixo de uma carga possivelmente já faturada.
    /// O guard é pela presença da carga, não pelo status — durante o faturamento parcial o
    /// romaneio ainda está Confirmed.
    /// </summary>
    [Fact]
    public async Task Execute_ComRomaneioMontadoEmCarga_Recusa()
    {
        var (contract, release) = await SeedAsync();
        var purchase = NewPurchase(release.Key, 1000m);
        var shipping = await CreateService().ExecuteAsync(contract.Key, purchase, "tester");

        var load = new ShipmentLoad
        {
            Key = Guid.NewGuid(),
            Code = "CG000007",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            TotalQuantity = 1000m,
            Status = ShipmentLoadStatus.Open,
        };
        _db.Context.ShipmentLoads.Add(load);

        var sales = await _db.Context.StorageTransactions
            .SingleAsync(x => x.Key == shipping.SalesStorageTransactionKey);
        sales.ShipmentLoadKey = load.Key;
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => ReverseService().ExecuteAsync(shipping.SalesStorageTransactionKey, "tester"));

        Assert.Contains("CG000007", error.Message);

        // Nada de estorno pela metade: o par continua vivo e o saldo da liberação intocado.
        var reloadedRelease = await _db.Context.ShipmentReleases
            .AsNoTracking().SingleAsync(x => x.Key == release.Key);
        Assert.Equal(1000m, reloadedRelease.ShippedQuantity);
    }

    [Fact]
    public async Task Execute_SemLiberacaoVinculada_NaoQuebra()
    {
        var (contract, _) = await SeedAsync();
        var purchase = NewPurchase(null, 1000m);
        var shipping = await CreateService().ExecuteAsync(contract.Key, purchase, "tester");

        // O estorno zera as FKs da ShippingTransaction antes de removê-la, e o teste
        // compartilha o DbContext — a chave precisa ser capturada antes da chamada.
        var purchaseKey = shipping.PurchaseStorageTransactionKey;

        await ReverseService().ExecuteAsync(shipping.SalesStorageTransactionKey, "tester");

        var reloaded = await _db.Context.StorageTransactions
            .AsNoTracking().SingleAsync(x => x.Key == purchaseKey);

        Assert.Equal(StorageTransactionsStatus.Cancelled, reloaded.TransactionStatus);
    }

    /// <summary>
    /// Embarque de liberação de transferência: sem perna de compra, o estorno tem de
    /// devolver o saldo lendo a liberação pela perna de SAÍDA. Lendo pela de compra (que
    /// não existe) o saldo ficaria preso — falha silenciosa, sem exceção nenhuma.
    /// </summary>
    [Fact]
    public async Task Execute_OwnershipTransferShipment_ReturnsTheReleaseBalance()
    {
        var (contract, release) = await SeedAsync(
            origin: ReleaseOrigin.OwnershipTransfer, lotCode: "LOTE-DEST");

        var shipping = await CreateService()
            .ExecuteAsync(contract.Key, NewPurchase(release.Key, 1000m), "tester");
        Assert.Null(shipping.PurchaseStorageTransactionKey);

        await ReverseService().ExecuteAsync(shipping.SalesStorageTransactionKey, "tester");

        var reloaded = await _db.Context.ShipmentReleases
            .AsNoTracking().SingleAsync(x => x.Key == release.Key);
        Assert.Equal(0m, reloaded.ShippedQuantity);
        Assert.Equal(1500m, reloaded.AvailableQuantity);
    }

    /// <summary>
    /// O contrato foi debitado pela TRANSFERÊNCIA, não por este embarque: estornar o
    /// embarque não pode devolver esse volume — o grão continua sendo da companhia.
    /// </summary>
    [Fact]
    public async Task Execute_OwnershipTransferShipment_LeavesTheContractAllocationAlone()
    {
        var (contract, release) = await SeedAsync(
            origin: ReleaseOrigin.OwnershipTransfer, lotCode: "LOTE-DEST");

        // Alocação escrita pelo confirm da transferência, apontando para outro romaneio.
        var transferPurchase = NewPurchase(releaseKey: null, grossWeight: 1000m);
        transferPurchase.NetWeight = 1000m;
        transferPurchase.TransactionStatus = StorageTransactionsStatus.Confirmed;
        _db.Context.StorageTransactions.Add(transferPurchase);
        _db.Context.PurchaseContractsAllocations.Add(new PurchaseContractAllocation
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = contract.Key,
            StorageTransactionKey = transferPurchase.Key,
            Volume = 1000m,
        });
        await _db.Context.SaveChangesAsync();

        var shipping = await CreateService()
            .ExecuteAsync(contract.Key, NewPurchase(release.Key, 1000m), "tester");

        await ReverseService().ExecuteAsync(shipping.SalesStorageTransactionKey, "tester");

        var allocation = Assert.Single(
            await _db.Context.PurchaseContractsAllocations.AsNoTracking().ToListAsync());
        Assert.Equal(1000m, allocation.Volume);
    }
}
