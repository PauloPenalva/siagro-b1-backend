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
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.Tests.ShippingTransactions;

/// <summary>
/// Cobre o efeito da Expedição de Grãos sobre a liberação de embarque. Todo o fluxo roda
/// em <c>CommitMode.Deferred</c>, e os hooks de <c>ShippedQuantity</c> em
/// StorageTransactionsCreate/Confirmed só disparam em <c>CommitMode.Auto</c> — sem um
/// recálculo explícito ao final, a liberação nunca era baixada.
/// </summary>
public class ShippingTransactionsCreateServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private ShipmentReleasesRecalculateShippedService Recalc() => new(_db.Context);

    private ShippingTransactionsCreateService CreateService(decimal lotBalance = 100_000m, IUnitOfWork? top = null)
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

        var allocationCreate = new PurchaseContractsAllocationCreateService(
            _db,
            new StorageTransactionsGetService(
                _db, NullLogger<StorageTransactionsGetService>.Instance));

        return new ShippingTransactionsCreateService(
            top ?? _db, storageCreate, storageConfirmed, storageCopy, allocationCreate, recalc,
            new FakeStorageAddressBalanceReader(lotBalance));
    }

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

    [Fact]
    public async Task Execute_BaixaSaldoDaLiberacaoDeEmbarque()
    {
        var (contract, release) = await SeedAsync();
        var purchase = NewPurchase(release.Key, 1000m);

        await CreateService().ExecuteAsync(contract.Key, purchase, "tester");

        var reloaded = await _db.Context.ShipmentReleases
            .AsNoTracking().SingleAsync(x => x.Key == release.Key);

        Assert.Equal(1000m, reloaded.ShippedQuantity);
        Assert.Equal(500m, reloaded.AvailableQuantity);
    }

    /// <summary>
    /// A expedição grava um PAR de romaneios (Purchase + a cópia retipada para
    /// SalesShipment), e a cópia herda o ShipmentReleaseKey. Só o lado da compra pode
    /// consumir a liberação — contar os dois dobraria o volume.
    /// </summary>
    [Fact]
    public async Task Execute_NaoContaACopiaDeVendaNoSaldoDaLiberacao()
    {
        var (contract, release) = await SeedAsync();
        var purchase = NewPurchase(release.Key, 1000m);

        await CreateService().ExecuteAsync(contract.Key, purchase, "tester");

        var pair = await _db.Context.StorageTransactions
            .AsNoTracking()
            .Where(x => x.ShipmentReleaseKey == release.Key)
            .ToListAsync();

        Assert.Equal(2, pair.Count);
        Assert.Single(pair, x => x.TransactionType == StorageTransactionType.SalesShipment);

        var reloaded = await _db.Context.ShipmentReleases
            .AsNoTracking().SingleAsync(x => x.Key == release.Key);

        Assert.Equal(1000m, reloaded.ShippedQuantity);
    }

    /// <summary>
    /// Descontos de qualidade não consomem a liberação: o eixo é o NetWeight da compra,
    /// o mesmo usado na alocação do contrato.
    /// </summary>
    [Fact]
    public async Task Execute_MedeSaldoPeloNetWeightDaCompra()
    {
        var (contract, release) = await SeedAsync();
        var purchase = NewPurchase(release.Key, 1000m);
        purchase.DryingDiscount = 30m;
        purchase.CleaningDiscount = 20m;

        await CreateService().ExecuteAsync(contract.Key, purchase, "tester");

        var reloaded = await _db.Context.ShipmentReleases
            .AsNoTracking().SingleAsync(x => x.Key == release.Key);

        Assert.Equal(950m, reloaded.ShippedQuantity);
    }

    [Fact]
    public async Task Execute_SemLiberacaoVinculada_NaoQuebra()
    {
        var (contract, _) = await SeedAsync();
        var purchase = NewPurchase(Guid.Empty, 1000m);
        purchase.ShipmentReleaseKey = null;

        var shipping = await CreateService().ExecuteAsync(contract.Key, purchase, "tester");

        Assert.NotEqual(Guid.Empty, shipping.Key);
    }

    [Fact]
    public async Task Auto_mode_owns_the_transaction_as_today()
    {
        var (contract, release) = await SeedAsync();
        var counting = new CountingUnitOfWork(_db);

        await CreateService(top: counting).ExecuteAsync(contract.Key, NewPurchase(release.Key, 1000m), "tester");

        Assert.Equal(1, counting.Begins);
        Assert.Equal(1, counting.Commits);
    }

    /// <summary>
    /// Deferred: quem chama é o dono da transação. O par é gravado (SaveChanges), mas a liberação
    /// NÃO é recalculada — o recálculo antes do commit contaria a cópia de venda (ver o comentário do
    /// serviço), então ele fica com quem chama, depois do commit dele.
    /// </summary>
    [Fact]
    public async Task Deferred_mode_does_not_touch_the_transaction_nor_recalculate_the_release()
    {
        var (contract, release) = await SeedAsync();
        var counting = new CountingUnitOfWork(_db);

        var shipping = await CreateService(top: counting)
            .ExecuteAsync(contract.Key, NewPurchase(release.Key, 1000m), "tester", CommitMode.Deferred);

        Assert.Equal(0, counting.Begins);
        Assert.Equal(0, counting.Commits);
        Assert.Equal(0, counting.Rollbacks);
        Assert.NotNull(await _db.Context.StorageTransactions.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Key == shipping.SalesStorageTransaction!.Key));
        Assert.Equal(0m, (await _db.Context.ShipmentReleases.AsNoTracking().SingleAsync(x => x.Key == release.Key)).ShippedQuantity);
    }
}
