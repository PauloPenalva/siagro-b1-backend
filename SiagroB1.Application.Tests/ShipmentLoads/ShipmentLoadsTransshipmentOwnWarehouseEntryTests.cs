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
/// GAC-1181 fase 2, Task 3 — a entrada do transbordo em armazém PRÓPRIO. A pesagem (Task 2) já
/// creditou o LOTE ao gerar o <see cref="StorageTransactionType.Receipt"/>; falta creditar o
/// ARMAZÉM, senão a Expedição do passo 6 (débito por <see cref="StorageTransactionType.SalesShipment"/>)
/// deixaria o armazém negativo. Esta classe substitui o alicerce da fase 1
/// (<c>ShipmentLoadsTransshipmentRegisterEntryServiceTests.RegisterEntry_OwnWarehouse_LinksTheExistingReceipt</c>,
/// atualizado nesta mesma task): aquele ramo aceitava qualquer <c>Receipt</c> confirmado do
/// armazém e não creditava nada.
/// </summary>
public class ShipmentLoadsTransshipmentOwnWarehouseEntryTests
{
    private const string OriginWarehouse = "ARM01";
    private const string TransshipmentWarehouse = "ARM99";
    private const string CardCode = "C0001";
    private const string TransshipmentLotCode = "L-TRANSSHIP-01";
    private const string RegularLotCode = "L-REGULAR-01";

    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private static FakeWarehouseService Warehouses() =>
        new(new Dictionary<string, string>
        {
            [OriginWarehouse] = "ARMAZEM ORIGEM",
            [TransshipmentWarehouse] = "ARMAZEM PROPRIO",
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
    /// Carga em transbordo para um armazém PRÓPRIO, com o romaneio de saída da origem que a levou
    /// até lá e o transbordo aberto (sem entrada registrada). É o estado em que a Task 4 (fase 1)
    /// deixa a carga.
    /// </summary>
    private async Task<(ShipmentLoad Load, ShipmentLoadTransshipment Transshipment, StorageTransaction Origin)>
        SeedStartedTransshipmentAsync(decimal outgoing = 30_000m, Guid? originReleaseKey = null)
    {
        _db.Context.WarehouseComplements.Add(new WarehouseComplement
        {
            WarehouseCode = TransshipmentWarehouse,
            IsOwn = true,
        });

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
            WarehouseName = "ARMAZEM PROPRIO",
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

    /// <summary>Grava um lote no armazém do transbordo com a natureza pedida.</summary>
    private async Task<StorageAddress> SeedLotAsync(string lotCode, StorageAddressNature nature)
    {
        var lot = new StorageAddress
        {
            Code = lotCode,
            Description = nature == StorageAddressNature.Transshipment
                ? "Lote de transbordo"
                : "Lote comum",
            CardCode = CardCode,
            ItemCode = "SOJA",
            WarehouseCode = TransshipmentWarehouse,
            UoM = "KG",
            Nature = nature,
        };
        _db.Context.StorageAddresses.Add(lot);
        await _db.SaveChangesAsync();
        return lot;
    }

    /// <summary>
    /// O <c>Receipt (0)</c> que a pesagem já lançou no armazém do transbordo, carregando o lote
    /// informado — exatamente como <c>WeighingTicketsCompletedService</c> o deixa (Task 2).
    /// </summary>
    private async Task<StorageTransaction> SeedReceiptAsync(
        ShipmentLoad load, string lotCode, decimal grossWeight)
    {
        var receipt = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = "E0001",
            CardCode = "F0001",
            ItemCode = load.ItemCode,
            UnitOfMeasureCode = load.UnitOfMeasureCode,
            WarehouseCode = TransshipmentWarehouse,
            BranchCode = load.BranchCode,
            StorageAddressCode = lotCode,
            GrossWeight = grossWeight,
            NetWeight = grossWeight,
            TransactionType = StorageTransactionType.Receipt,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
        };
        _db.Context.StorageTransactions.Add(receipt);
        await _db.SaveChangesAsync();
        return receipt;
    }

    [Fact]
    public async Task RegisterEntry_OwnWarehouse_RefusesReceiptFromRegularLot()
    {
        var (load, transshipment, _) = await SeedStartedTransshipmentAsync(outgoing: 30_000);
        await SeedLotAsync(RegularLotCode, StorageAddressNature.Regular);
        var receipt = await SeedReceiptAsync(load, RegularLotCode, 29_800m);

        var error = await Assert.ThrowsAnyAsync<Exception>(
            () => Service().ExecuteAsync(
                transshipment.Key!.Value, decimal.Zero, DateTime.Today, receipt.Key, "tester"));

        Assert.Contains("lote de", error.Message);
        Assert.Contains("Transbordo", error.Message);
    }

    [Fact]
    public async Task RegisterEntry_OwnWarehouse_LinksReceiptFromTransshipmentLot()
    {
        var (load, transshipment, _) = await SeedStartedTransshipmentAsync(outgoing: 30_000);
        await SeedLotAsync(TransshipmentLotCode, StorageAddressNature.Transshipment);
        var receipt = await SeedReceiptAsync(load, TransshipmentLotCode, 29_800m);

        var result = await Service().ExecuteAsync(
            transshipment.Key!.Value, decimal.Zero, DateTime.Today, receipt.Key, "tester");

        Assert.Equal(29_800m, result.EntryQuantity);
        Assert.Equal(receipt.Key, result.EntryStorageTransactionKey);

        var linked = await _db.Context.StorageTransactions
            .AsNoTracking()
            .SingleAsync(x => x.Key == receipt.Key);

        Assert.Equal(transshipment.Key, linked.ShipmentLoadTransshipmentKey);
    }

    [Fact]
    public async Task RegisterEntry_OwnWarehouse_CreatesTheWarehouseCreditReceipt()
    {
        var (load, transshipment, _) = await SeedStartedTransshipmentAsync(outgoing: 30_000);
        await SeedLotAsync(TransshipmentLotCode, StorageAddressNature.Transshipment);
        var receipt = await SeedReceiptAsync(load, TransshipmentLotCode, 29_800m);

        await Service().ExecuteAsync(
            transshipment.Key!.Value, decimal.Zero, DateTime.Today, receipt.Key, "tester");

        var credit = await _db.Context.StorageTransactions
            .AsNoTracking()
            .SingleAsync(x => x.TransactionType == StorageTransactionType.TransshipmentReceipt);

        Assert.Equal(TransshipmentWarehouse, credit.WarehouseCode);
        Assert.Equal(receipt.GrossWeight, credit.GrossWeight);
        Assert.Equal(StorageTransactionsStatus.Confirmed, credit.TransactionStatus);
        Assert.Null(credit.StorageAddressCode);
    }

    /// <summary>
    /// O teste da armadilha (mesmo molde do armazém de terceiro,
    /// <c>ShipmentLoadsTransshipmentRegisterEntryServiceTests.RegisterEntry_TheReceiptCarriesNeitherReleaseKeyNorLoadKey</c>):
    /// o romaneio que ORIGINA uma liberação nunca a consome, e o crédito do armazém não pode
    /// carregar a <c>ShipmentLoadKey</c> da carga (o cancelamento dela zeraria a chave em todos os
    /// romaneios e deixaria o crédito órfão).
    /// </summary>
    /// <remarks>
    /// A origem PRECISA carregar uma <c>ShipmentReleaseKey</c> real (não nula) para a metade
    /// <c>ShipmentReleaseKey</c> da asserção provar algo: com a origem nula, um bug que copiasse a
    /// chave da origem passaria pelo teste do mesmo jeito, porque o valor copiado também seria
    /// nulo.
    /// </remarks>
    [Fact]
    public async Task RegisterEntry_OwnWarehouse_TheCreditCarriesNeitherReleaseKeyNorLoadKey()
    {
        var contract = NewContract();
        var originRelease = NewOriginRelease(contract.Key);
        var (load, transshipment, _) = await SeedStartedTransshipmentAsync(
            outgoing: 30_000, originReleaseKey: originRelease.Key);
        await SeedLotAsync(TransshipmentLotCode, StorageAddressNature.Transshipment);
        var receipt = await SeedReceiptAsync(load, TransshipmentLotCode, 29_800m);

        await Service().ExecuteAsync(
            transshipment.Key!.Value, decimal.Zero, DateTime.Today, receipt.Key, "tester");

        var credit = await _db.Context.StorageTransactions
            .AsNoTracking()
            .SingleAsync(x => x.TransactionType == StorageTransactionType.TransshipmentReceipt);

        Assert.Null(credit.ShipmentReleaseKey);
        Assert.Null(credit.ShipmentLoadKey);
        Assert.Equal(transshipment.Key, credit.ShipmentLoadTransshipmentKey);
    }

    [Fact]
    public async Task RegisterEntry_OwnWarehouse_DoesNotEmitRelease()
    {
        var contract = NewContract();
        var originRelease = NewOriginRelease(contract.Key);
        var (load, transshipment, _) = await SeedStartedTransshipmentAsync(
            outgoing: 30_000, originReleaseKey: originRelease.Key);
        await SeedLotAsync(TransshipmentLotCode, StorageAddressNature.Transshipment);
        var receipt = await SeedReceiptAsync(load, TransshipmentLotCode, 29_800m);

        await Service().ExecuteAsync(
            transshipment.Key!.Value, decimal.Zero, DateTime.Today, receipt.Key, "tester");

        Assert.Empty(await _db.Context.ShipmentReleases
            .AsNoTracking()
            .Where(x => x.Origin == ReleaseOrigin.Transshipment)
            .ToListAsync());
    }
}
