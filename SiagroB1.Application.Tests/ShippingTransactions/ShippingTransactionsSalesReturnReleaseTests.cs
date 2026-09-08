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
/// Reembarque, pela Expedição de Grãos, de mercadoria que voltou a um armazém.
/// A liberação de <see cref="ReleaseOrigin.SalesReturn"/> tem o físico já em nosso poder —
/// creditado pelo romaneio <see cref="StorageTransactionType.SalesShipmentReturn"/> — então a
/// Expedição cria só a perna de SAÍDA. Criar o <c>Purchase(8)</c> aqui creditaria o armazém
/// uma segunda vez por grão que está saindo e alocaria de novo um contrato que já foi debitado.
/// </summary>
public class ShippingTransactionsSalesReturnReleaseTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private ShippingTransactionsCreateService CreateService()
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
            new FakeStorageAddressBalanceReader(100_000m));
    }

    /// <summary>
    /// Semeia o estado depois de uma devolução: o romaneio tipo 12 confirmado creditando o
    /// armazém, e a liberação que ele emitiu — <b>sem lote</b> e, no romaneio,
    /// <b>sem <c>ShipmentReleaseKey</c></b>, que é o que impede o saldo de nascer negativo.
    /// </summary>
    private async Task<(PurchaseContract Contract, ShipmentRelease Release, StorageTransaction Entry)>
        SeedReturnedGoodsAsync(decimal quantity = 1500m)
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

        var entry = new StorageTransaction
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
            // as três chaves proibidas continuam nulas; ShipmentReleaseKey em especial
        };

        var release = new ShipmentRelease
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = contract.Key,
            DeliveryLocationCode = "01",
            ReleasedQuantity = quantity,
            ShippedQuantity = 0m,
            Status = ReleaseStatus.Actived,
            Origin = ReleaseOrigin.SalesReturn,
            StorageAddressCode = null,
            GeneratedByStorageTransactionKey = entry.Key,
        };

        _db.Context.PurchaseContracts.Add(contract);
        _db.Context.StorageTransactions.Add(entry);
        _db.Context.ShipmentReleases.Add(release);
        await _db.Context.SaveChangesAsync();

        return (contract, release, entry);
    }

    private static StorageTransaction NewPurchasePayload(Guid releaseKey, decimal grossWeight) => new()
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
    public async Task Execute_SalesReturnRelease_CreatesOnlyTheSalesShipmentLeg()
    {
        var (contract, release, _) = await SeedReturnedGoodsAsync();

        var shipping = await CreateService()
            .ExecuteAsync(contract.Key, NewPurchasePayload(release.Key, 1000m), "tester");

        var legs = await _db.Context.StorageTransactions
            .AsNoTracking().Where(x => x.ShipmentReleaseKey == release.Key).ToListAsync();

        var sales = Assert.Single(legs);
        Assert.Equal(StorageTransactionType.SalesShipment, sales.TransactionType);
        Assert.Null(shipping.PurchaseStorageTransactionKey);
    }

    /// <summary>
    /// ⚠️ Trava da linha do guard de lote. O romaneio tipo 12 nasce sem
    /// <c>StorageAddressCode</c> (a devolução é entrada em nível de ARMAZÉM), então a liberação
    /// também não tem lote. Se alguém generalizar o guard de
    /// <c>ResolveReleaseLotAsync</c> para "toda origem != Standard", todo reembarque de
    /// devolução passa a estourar "liberação de transferência sem lote".
    /// </summary>
    [Fact]
    public async Task Execute_SalesReturnRelease_DoesNotRequireAStorageLot()
    {
        var (contract, release, _) = await SeedReturnedGoodsAsync();

        await CreateService()
            .ExecuteAsync(contract.Key, NewPurchasePayload(release.Key, 1000m), "tester");

        var sales = await _db.Context.StorageTransactions
            .AsNoTracking().SingleAsync(x => x.ShipmentReleaseKey == release.Key);

        Assert.Null(sales.StorageAddressCode);
    }

    /// <summary>
    /// A restrição do usuário no eixo da ALOCAÇÃO: o contrato já foi debitado quando a
    /// mercadoria saiu pela primeira vez. Um segundo débito pelo mesmo grão dobraria o consumo.
    /// </summary>
    [Fact]
    public async Task Execute_SalesReturnRelease_DoesNotAllocateTheContract()
    {
        var (contract, release, _) = await SeedReturnedGoodsAsync();

        await CreateService()
            .ExecuteAsync(contract.Key, NewPurchasePayload(release.Key, 1000m), "tester");

        Assert.Empty(await _db.Context.PurchaseContractsAllocations.AsNoTracking().ToListAsync());

        var reloaded = await _db.Context.PurchaseContracts
            .AsNoTracking().SingleAsync(x => x.Key == contract.Key);
        Assert.Equal(0m, reloaded.AllocatedVolume);
    }

    [Fact]
    public async Task Execute_SalesReturnRelease_ConsumesTheReleaseThroughTheSalesLeg()
    {
        var (contract, release, _) = await SeedReturnedGoodsAsync();

        await CreateService()
            .ExecuteAsync(contract.Key, NewPurchasePayload(release.Key, 1000m), "tester");

        var reloaded = await _db.Context.ShipmentReleases
            .AsNoTracking().SingleAsync(x => x.Key == release.Key);

        Assert.Equal(1000m, reloaded.ShippedQuantity);
        Assert.Equal(500m, reloaded.AvailableQuantity); // 1500 devolvidos − 1000 reembarcados
    }

    /// <summary>
    /// O fecho do ciclo físico: a devolução creditou +Q no armazém e o reembarque debita −Q.
    /// Se a Expedição criasse o <c>Purchase(8)</c>, sobraria um +Q fantasma permanente — o
    /// mesmo defeito que o "desenho 2" da transferência de titularidade corrigiu.
    /// </summary>
    [Fact]
    public async Task Execute_SalesReturnRelease_LeavesWarehouseBalanceAtZero()
    {
        var (contract, release, _) = await SeedReturnedGoodsAsync(quantity: 1500m);

        await CreateService()
            .ExecuteAsync(contract.Key, NewPurchasePayload(release.Key, 1500m), "tester");

        var balance = await StorageTransactionsWarehouseBalanceService
            .CalculateAsync(_db.Context, "01", "SOJA");

        Assert.Equal(0m, balance); // +1500 (tipo 12) − 1500 (tipo 7)
    }

    /// <summary>
    /// A liberação de devolução dispensa o contrato no payload — como a de transferência.
    /// </summary>
    [Fact]
    public async Task Execute_SalesReturnRelease_AcceptsNullPurchaseContractKey()
    {
        var (_, release, _) = await SeedReturnedGoodsAsync();

        var shipping = await CreateService()
            .ExecuteAsync(null, NewPurchasePayload(release.Key, 1000m), "tester");

        Assert.Null(shipping.PurchaseStorageTransactionKey);
    }

    /// <summary>
    /// Não-regressão: a liberação comum continua exigindo contrato e criando o par
    /// Purchase(8)/SalesShipment(7). É a metade da linha 43 que não pode mudar.
    /// </summary>
    [Fact]
    public async Task Execute_StandardRelease_StillRequiresThePurchaseContract()
    {
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(),
            Code = "PC-002",
            CardCode = "F0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "2026",
            DeliveryLocationCode = "01",
            Status = ContractStatus.Approved,
            TotalVolume = 10_000m,
        };
        var release = new ShipmentRelease
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = contract.Key,
            DeliveryLocationCode = "01",
            ReleasedQuantity = 1500m,
            Status = ReleaseStatus.Actived,
            Origin = ReleaseOrigin.Standard,
        };
        _db.Context.PurchaseContracts.Add(contract);
        _db.Context.ShipmentReleases.Add(release);
        await _db.Context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => CreateService().ExecuteAsync(null, NewPurchasePayload(release.Key, 1000m), "tester"));

        Assert.Contains("Contrato de compra é obrigatório", ex.Message);
    }
}
