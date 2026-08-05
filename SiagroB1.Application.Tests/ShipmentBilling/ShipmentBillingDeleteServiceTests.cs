using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Services.ShipmentBilling;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.ShippingTransactions;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentBilling;

/// <summary>
/// Estorno do romaneio de embarque (tela /shipment-billing). O estorno cancela o par
/// Purchase/SalesShipment escrevendo o status direto — não passa por
/// <c>StorageTransactionsCancelService</c>, que é onde vive o hook de
/// <c>ShippedQuantity</c>. Sem um recálculo explícito, o saldo da liberação de embarque
/// fica preso no valor de antes do estorno.
/// </summary>
public class ShipmentBillingDeleteServiceTests
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

    private ShipmentBillingDeleteService DeleteService() =>
        new(_db,
            GetService(),
            new PurchaseContractsAllocationDeleteService(
                _db, NullLogger<PurchaseContractsAllocationDeleteService>.Instance),
            Recalc(),
            NullLogger<ShipmentBillingDeleteService>.Instance);

    /// <summary>Contrato aprovado com uma liberação ativa e saldo de sobra.</summary>
    private async Task<(PurchaseContract Contract, ShipmentRelease Release)> SeedAsync(
        decimal totalVolume = 10000m,
        decimal releasedQuantity = 1500m)
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
        };

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

        await DeleteService().ExecuteAsync(shipping.SalesStorageTransactionKey, "tester");

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

        await DeleteService().ExecuteAsync(shipping.SalesStorageTransactionKey, "tester");

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

        await DeleteService().ExecuteAsync(first.SalesStorageTransactionKey, "tester");

        var reloaded = await _db.Context.ShipmentReleases
            .AsNoTracking().SingleAsync(x => x.Key == release.Key);

        Assert.Equal(400m, reloaded.ShippedQuantity);
        Assert.Equal(1100m, reloaded.AvailableQuantity);
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

        await DeleteService().ExecuteAsync(shipping.SalesStorageTransactionKey, "tester");

        var reloaded = await _db.Context.StorageTransactions
            .AsNoTracking().SingleAsync(x => x.Key == purchaseKey);

        Assert.Equal(StorageTransactionsStatus.Cancelled, reloaded.TransactionStatus);
    }
}
