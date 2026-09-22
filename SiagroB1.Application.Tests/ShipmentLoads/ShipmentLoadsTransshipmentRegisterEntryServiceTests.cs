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
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// Registrar a entrada do transbordo (GAC-1181, Task 5): o romaneio 15 em armazém de terceiro
/// (com as liberações que devolvem a mercadoria à Expedição de Grãos), ou o vínculo do Receipt já
/// existente em armazém próprio. Iniciar é a Task 4, estornar é a Task 6 — nenhuma das duas é
/// exercida aqui.
/// </summary>
public class ShipmentLoadsTransshipmentRegisterEntryServiceTests
{
    private const string OriginWarehouse = "ARM01";
    private const string TransshipmentWarehouse = "ARM99";
    private const string CardCode = "C0001";

    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private static FakeWarehouseService Warehouses() =>
        new(new Dictionary<string, string>
        {
            [OriginWarehouse] = "ARMAZEM ORIGEM",
            [TransshipmentWarehouse] = "ARMAZEM TERCEIRO",
        });

    private StorageTransactionsCreateService StorageCreate() =>
        new(_db,
            new FakeDocNumberSequenceService(),
            new FakeBusinessPartnerService(names: new Dictionary<string, string> { [CardCode] = "CLIENTE TESTE" }),
            new FakeItemService(new Dictionary<string, string> { ["SOJA"] = "SOJA EM GRAOS" }),
            Warehouses(),
            new ShipmentReleasesRecalculateShippedService(_db.Context),
            new ShipmentReleaseMovementGuardService(_db.Context),
            NullLogger<StorageTransactionsCreateService>.Instance);

    private StorageTransactionsConfirmedService StorageConfirm() =>
        new(_db,
            new FakeStringLocalizer<Resource>(),
            new ShipmentReleasesRecalculateShippedService(_db.Context),
            new ShipmentReleaseMovementGuardService(_db.Context),
            NullLogger<StorageTransactionsConfirmedService>.Instance);

    private ShipmentLoadsTransshipmentRegisterEntryService Service() => new(
        _db,
        Warehouses(),
        new WarehouseComplementService(_db),
        StorageCreate(),
        StorageConfirm(),
        new ShipmentReleasesFromReturnService(_db.Context),
        new ShipmentLoadsMovementLogService(_db.Context));

    /// <summary>
    /// Carga em transbordo, com o romaneio de saída da origem que a levou até lá e o transbordo
    /// aberto (sem entrada registrada). É o estado em que a Task 4 deixa a carga.
    /// </summary>
    private async Task<(ShipmentLoad Load, ShipmentLoadTransshipment Transshipment, StorageTransaction Origin)>
        SeedStartedTransshipmentAsync(decimal outgoing = 30_000m, Guid? originReleaseKey = null)
    {
        var load = new ShipmentLoad
        {
            Key = Guid.NewGuid(),
            Code = "CG000001",
            BranchCode = "01",
            ItemCode = "SOJA",
            ItemName = "SOJA EM GRAOS",
            UnitOfMeasureCode = "KG",
            TruckCode = "ABC1D23",
            WarehouseCode = OriginWarehouse,
            Status = ShipmentLoadStatus.InTransshipment,
            TotalQuantity = outgoing,
        };

        var origin = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = "R0001",
            CardCode = CardCode,
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            WarehouseCode = OriginWarehouse,
            BranchCode = "01",
            TruckCode = "ABC1D23",
            GrossWeight = outgoing,
            NetWeight = outgoing,
            TransactionType = StorageTransactionType.SalesShipment,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
            ShipmentLoadKey = load.Key,
            ShipmentReleaseKey = originReleaseKey,
        };

        var transshipment = new ShipmentLoadTransshipment
        {
            ShipmentLoadKey = load.Key,
            Sequence = 1,
            WarehouseCode = TransshipmentWarehouse,
            WarehouseName = "ARMAZEM TERCEIRO",
            OutgoingQuantity = outgoing,
        };

        _db.Context.ShipmentLoads.Add(load);
        _db.Context.StorageTransactions.Add(origin);
        _db.Context.ShipmentLoadsTransshipments.Add(transshipment);
        await _db.SaveChangesAsync();

        return (load, transshipment, origin);
    }

    private PurchaseContract NewContract(string code = "PC-001", string itemCode = "SOJA")
    {
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(),
            Code = code,
            CardCode = "F0001",
            ItemCode = itemCode,
            UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "2026",
            DeliveryLocationCode = OriginWarehouse,
            BranchCode = "01",
            Status = ContractStatus.Approved,
            TotalVolume = 1_000_000m,
        };
        _db.Context.PurchaseContracts.Add(contract);
        return contract;
    }

    private ShipmentRelease NewOriginRelease(Guid contractKey)
    {
        var release = new ShipmentRelease
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = contractKey,
            DeliveryLocationCode = OriginWarehouse,
            ReleasedQuantity = 1_000_000m,
            Status = ReleaseStatus.Actived,
        };
        _db.Context.ShipmentReleases.Add(release);
        return release;
    }

    private Task<ShipmentLoadTransshipment> TransshipmentAsync(Guid key) =>
        _db.Context.ShipmentLoadsTransshipments.AsNoTracking().SingleAsync(x => x.Key == key);

    // ---------- armazém de terceiro ----------

    [Fact]
    public async Task RegisterEntry_CreatesTheReceiptWithTheWeighedQuantity()
    {
        var (_, transshipment, _) = await SeedStartedTransshipmentAsync(outgoing: 30_000);

        await Service().ExecuteAsync(
            transshipment.Key!.Value, 29_800m, null, DateTime.Today, null, "tester");

        var entry = await _db.Context.StorageTransactions
            .AsNoTracking()
            .SingleAsync(x => x.TransactionType == StorageTransactionType.TransshipmentReceipt);

        Assert.Equal(TransshipmentWarehouse, entry.WarehouseCode);
        Assert.Equal(29_800m, entry.GrossWeight);
        Assert.Equal(29_800m, entry.NetWeight);
        Assert.Equal(StorageTransactionsStatus.Confirmed, entry.TransactionStatus);

        var saved = await TransshipmentAsync(transshipment.Key!.Value);
        Assert.Equal(29_800m, saved.EntryQuantity);
        Assert.Equal(entry.Key, saved.EntryStorageTransactionKey);
    }

    /// <summary>
    /// O teste da armadilha (decisão do GAC-1181): o romaneio que ORIGINA uma liberação nunca a
    /// consome, e a carga o alcança só por <c>ShipmentLoadTransshipmentKey</c> — nunca por
    /// <c>ShipmentLoadKey</c>, que o cancelamento zeraria e deixaria a entrada órfã.
    /// </summary>
    /// <remarks>
    /// A origem PRECISA carregar uma <c>ShipmentReleaseKey</c> real (não nula) para a metade
    /// <c>ShipmentReleaseKey</c> da asserção provar algo: com a origem nula, um bug que copiasse
    /// a chave da origem passaria pelo teste do mesmo jeito, porque o valor copiado também seria
    /// nulo. Mesmo precedente de
    /// <c>ShipmentLoadsRefuseServiceTests.A_refusal_release_is_linked_to_the_return_shipment_and_not_to_the_load</c>.
    /// </remarks>
    [Fact]
    public async Task RegisterEntry_TheReceiptCarriesNeitherReleaseKeyNorLoadKey()
    {
        var contract = NewContract();
        var originRelease = NewOriginRelease(contract.Key);
        var (_, transshipment, _) = await SeedStartedTransshipmentAsync(
            outgoing: 30_000, originReleaseKey: originRelease.Key);

        await Service().ExecuteAsync(
            transshipment.Key!.Value, 29_800m, null, DateTime.Today, null, "tester");

        var entry = await _db.Context.StorageTransactions
            .AsNoTracking()
            .SingleAsync(x => x.TransactionType == StorageTransactionType.TransshipmentReceipt);

        Assert.Null(entry.ShipmentReleaseKey);
        Assert.Null(entry.ShipmentLoadKey);
        Assert.Equal(transshipment.Key, entry.ShipmentLoadTransshipmentKey);
    }

    [Fact]
    public async Task RegisterEntry_EmitsOneReleasePerPurchaseContract()
    {
        var (load, transshipment, origin) = await SeedStartedTransshipmentAsync(outgoing: 30_000);

        var contractA = NewContract("PC-A");
        var contractB = NewContract("PC-B");
        var releaseA = NewOriginRelease(contractA.Key);
        var releaseB = NewOriginRelease(contractB.Key);

        // Um segundo romaneio de saída da origem, de outro contrato — a carga levou grão dos
        // dois fornecedores.
        var origin2 = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = "R0002",
            CardCode = "C0002",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            WarehouseCode = OriginWarehouse,
            BranchCode = "01",
            TruckCode = "ABC1D23",
            GrossWeight = 10_000m,
            NetWeight = 10_000m,
            TransactionType = StorageTransactionType.SalesShipment,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
            ShipmentLoadKey = load.Key,
            ShipmentReleaseKey = releaseB.Key,
        };
        _db.Context.StorageTransactions.Add(origin2);

        var tracked = await _db.Context.StorageTransactions.SingleAsync(x => x.Key == origin.Key);
        tracked.GrossWeight = 20_000m;
        tracked.NetWeight = 20_000m;
        tracked.ShipmentReleaseKey = releaseA.Key;
        await _db.SaveChangesAsync();

        await Service().ExecuteAsync(
            transshipment.Key!.Value, 30_000m, null, DateTime.Today, null, "tester");

        var releases = await _db.Context.ShipmentReleases
            .AsNoTracking()
            .Where(x => x.Origin == ReleaseOrigin.Transshipment)
            .ToListAsync();

        Assert.Equal(2, releases.Count);
        Assert.Contains(releases, x => x.PurchaseContractKey == contractA.Key);
        Assert.Contains(releases, x => x.PurchaseContractKey == contractB.Key);
    }

    [Fact]
    public async Task RegisterEntry_ReleasesDoNotConsumeThePurchaseContract()
    {
        var (_, transshipment, origin) = await SeedStartedTransshipmentAsync(outgoing: 30_000);

        var contract = NewContract();
        var release = NewOriginRelease(contract.Key);
        var tracked = await _db.Context.StorageTransactions.SingleAsync(x => x.Key == origin.Key);
        tracked.ShipmentReleaseKey = release.Key;
        await _db.SaveChangesAsync();

        await Service().ExecuteAsync(
            transshipment.Key!.Value, 30_000m, null, DateTime.Today, null, "tester");

        var created = await _db.Context.ShipmentReleases
            .AsNoTracking()
            .SingleAsync(x => x.Origin == ReleaseOrigin.Transshipment);

        Assert.Equal(decimal.Zero, created.ConsumedQuantity);
    }

    [Fact]
    public async Task RegisterEntry_RecordsTheShrinkage()
    {
        var (_, transshipment, _) = await SeedStartedTransshipmentAsync(outgoing: 30_000);

        var result = await Service().ExecuteAsync(
            transshipment.Key!.Value, 29_800m, null, DateTime.Today, null, "tester");

        Assert.Equal(200m, result.ShrinkageQuantity);
    }

    [Fact]
    public async Task RegisterEntry_RefusesWhenAlreadyRegistered()
    {
        var (_, transshipment, _) = await SeedStartedTransshipmentAsync(outgoing: 30_000);

        await Service().ExecuteAsync(
            transshipment.Key!.Value, 29_800m, null, DateTime.Today, null, "tester");

        var error = await Assert.ThrowsAnyAsync<Exception>(
            () => Service().ExecuteAsync(
                transshipment.Key!.Value, 29_800m, null, DateTime.Today, null, "tester"));

        Assert.Contains("já foi registrada", error.Message);
    }

    [Fact]
    public async Task RegisterEntry_RefusesQuantityGreaterThanOutgoing()
    {
        var (_, transshipment, _) = await SeedStartedTransshipmentAsync(outgoing: 30_000);

        var error = await Assert.ThrowsAnyAsync<Exception>(
            () => Service().ExecuteAsync(
                transshipment.Key!.Value, 30_001m, null, DateTime.Today, null, "tester"));

        Assert.Contains("maior que o volume transbordado", error.Message);
    }

    [Fact]
    public async Task RegisterEntry_UntraceableVolume_DoesNotFail()
    {
        // Sem NewContract/NewOriginRelease: o romaneio de origem não tem ShipmentReleaseKey nem
        // par em SHIPPING_TRANSACTIONS — nenhuma das duas cadeias resolve um contrato.
        var (_, transshipment, _) = await SeedStartedTransshipmentAsync(outgoing: 30_000);

        await Service().ExecuteAsync(
            transshipment.Key!.Value, 29_800m, null, DateTime.Today, null, "tester");

        Assert.Empty(await _db.Context.ShipmentReleases
            .AsNoTracking()
            .Where(x => x.Origin == ReleaseOrigin.Transshipment)
            .ToListAsync());

        var entry = await _db.Context.StorageTransactions
            .AsNoTracking()
            .SingleAsync(x => x.TransactionType == StorageTransactionType.TransshipmentReceipt);

        Assert.Equal(StorageTransactionsStatus.Confirmed, entry.TransactionStatus);
        Assert.Contains("sem contrato de compra rastreável", entry.Comments);
        Assert.True(entry.Comments!.Length <= 500);
    }

    // ---------- armazém próprio ----------

    [Fact]
    public async Task RegisterEntry_OwnWarehouse_LinksTheExistingReceipt()
    {
        var (load, transshipment, _) = await SeedStartedTransshipmentAsync(outgoing: 30_000);

        _db.Context.WarehouseComplements.Add(new WarehouseComplement
        {
            WarehouseCode = TransshipmentWarehouse,
            IsOwn = true,
        });

        var receipt = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = "E0001",
            CardCode = "F0001",
            ItemCode = load.ItemCode,
            UnitOfMeasureCode = load.UnitOfMeasureCode,
            WarehouseCode = TransshipmentWarehouse,
            BranchCode = load.BranchCode,
            GrossWeight = 29_800m,
            NetWeight = 29_800m,
            TransactionType = StorageTransactionType.Receipt,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
        };
        _db.Context.StorageTransactions.Add(receipt);
        await _db.SaveChangesAsync();

        var result = await Service().ExecuteAsync(
            transshipment.Key!.Value, decimal.Zero, null, DateTime.Today, receipt.Key, "tester");

        Assert.Equal(29_800m, result.EntryQuantity);
        Assert.Equal(receipt.Key, result.EntryStorageTransactionKey);

        var linked = await _db.Context.StorageTransactions
            .AsNoTracking()
            .SingleAsync(x => x.Key == receipt.Key);

        Assert.Equal(transshipment.Key, linked.ShipmentLoadTransshipmentKey);

        // Não nasce romaneio novo nem liberação: o grão entrou no LOTE pela Entrada em
        // Armazenagem, não pela Expedição de Grãos.
        Assert.Empty(await _db.Context.StorageTransactions
            .AsNoTracking()
            .Where(x => x.TransactionType == StorageTransactionType.TransshipmentReceipt)
            .ToListAsync());
        Assert.Empty(await _db.Context.ShipmentReleases
            .AsNoTracking()
            .Where(x => x.Origin == ReleaseOrigin.Transshipment)
            .ToListAsync());
    }
}
