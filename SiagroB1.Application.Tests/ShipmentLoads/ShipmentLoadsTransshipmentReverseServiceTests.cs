using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.StorageAddresses;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// Estornar o transbordo (GAC-1181, Task 6): par de
/// <see cref="ShipmentLoadsTransshipmentRegisterEntryService"/> (Task 5) — desfaz a entrada
/// registrada (ou apenas remove o transbordo, se a entrada ainda não tiver sido registrada) e
/// devolve o volume à carga.
/// </summary>
public class ShipmentLoadsTransshipmentReverseServiceTests
{
    private const string OriginWarehouse = "ARM01";
    private const string TransshipmentWarehouse = "ARM99";
    private const string CardCode = "C0001";

    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();

    private ShipmentLoadsTransshipmentReverseService Service() =>
        new(_db, new ShipmentLoadsMovementLogService(_db.Context));

    private ShipmentLoad Load(decimal totalQuantity = 30_000m)
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
            TotalQuantity = totalQuantity,
            TransshippedQuantity = totalQuantity,
        };
        _db.Context.ShipmentLoads.Add(load);
        return load;
    }

    private PurchaseContract NewContract(string code = "PC-001")
    {
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(),
            Code = code,
            CardCode = "F0001",
            ItemCode = "SOJA",
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

    /// <summary>
    /// Transbordo em armazém de TERCEIRO, com a entrada já registrada (romaneio 15 confirmado) e
    /// uma liberação de origem Transshipment gerada por ele — o estado em que a Task 5 deixa a
    /// carga.
    /// </summary>
    private async Task<(ShipmentLoad Load, ShipmentLoadTransshipment Transshipment,
            StorageTransaction Entry, ShipmentRelease Release)>
        SeedThirdPartyEntryRegisteredAsync(
            decimal outgoing = 30_000m, int sequence = 1, decimal shippedQuantity = 0m,
            TransshipmentOrigin origin = TransshipmentOrigin.Planned)
    {
        var load = Load(outgoing);

        var entry = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = "R0015",
            CardCode = CardCode,
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            WarehouseCode = TransshipmentWarehouse,
            BranchCode = "01",
            TruckCode = "ABC1D23",
            GrossWeight = outgoing,
            NetWeight = outgoing,
            TransactionType = StorageTransactionType.TransshipmentReceipt,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
        };

        var transshipment = new ShipmentLoadTransshipment
        {
            ShipmentLoadKey = load.Key,
            Sequence = sequence,
            Origin = origin,
            WarehouseCode = TransshipmentWarehouse,
            WarehouseName = "ARMAZEM TERCEIRO",
            OutgoingQuantity = outgoing,
            EntryQuantity = outgoing,
        };

        _db.Context.StorageTransactions.Add(entry);
        _db.Context.ShipmentLoadsTransshipments.Add(transshipment);
        await _db.Context.SaveChangesAsync();

        // Só agora o Key gerado pelo InMemory está disponível para fechar as duas pontas.
        entry.ShipmentLoadTransshipmentKey = transshipment.Key;
        transshipment.EntryStorageTransactionKey = entry.Key;

        var contract = NewContract();
        var release = new ShipmentRelease
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = contract.Key,
            DeliveryLocationCode = TransshipmentWarehouse,
            ReleasedQuantity = outgoing,
            ShippedQuantity = shippedQuantity,
            Status = ReleaseStatus.Actived,
            Origin = ReleaseOrigin.Transshipment,
            GeneratedByStorageTransactionKey = entry.Key,
        };
        _db.Context.ShipmentReleases.Add(release);

        await _db.Context.SaveChangesAsync();

        return (load, transshipment, entry, release);
    }

    /// <summary>
    /// Transbordo em armazém PRÓPRIO, com a entrada já registrada: só o vínculo no <c>Receipt</c>
    /// de uma Entrada em Armazenagem existente — sem romaneio novo nem liberação.
    /// </summary>
    private async Task<(ShipmentLoad Load, ShipmentLoadTransshipment Transshipment, StorageTransaction Receipt)>
        SeedOwnWarehouseEntryRegisteredAsync(decimal outgoing = 30_000m)
    {
        var load = Load(outgoing);

        var receipt = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = "E0001",
            CardCode = "F0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            WarehouseCode = TransshipmentWarehouse,
            BranchCode = "01",
            GrossWeight = outgoing,
            NetWeight = outgoing,
            TransactionType = StorageTransactionType.Receipt,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
        };

        var transshipment = new ShipmentLoadTransshipment
        {
            ShipmentLoadKey = load.Key,
            Sequence = 1,
            WarehouseCode = TransshipmentWarehouse,
            WarehouseName = "ARMAZEM PROPRIO",
            OutgoingQuantity = outgoing,
            EntryQuantity = outgoing,
        };

        _db.Context.StorageTransactions.Add(receipt);
        _db.Context.ShipmentLoadsTransshipments.Add(transshipment);
        await _db.Context.SaveChangesAsync();

        receipt.ShipmentLoadTransshipmentKey = transshipment.Key;
        transshipment.EntryStorageTransactionKey = receipt.Key;
        await _db.Context.SaveChangesAsync();

        return (load, transshipment, receipt);
    }

    /// <summary>Romaneio de saída que FECHA um transbordo — vinculação que a Task 7 fará de verdade.</summary>
    private void LinkExit(Guid loadKey, Guid transshipmentKey)
    {
        _db.Context.StorageTransactions.Add(new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = "R-RECARGA",
            CardCode = CardCode,
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            WarehouseCode = TransshipmentWarehouse,
            BranchCode = "01",
            GrossWeight = 1_000m,
            NetWeight = 1_000m,
            TransactionType = StorageTransactionType.SalesShipment,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
            ShipmentLoadKey = loadKey,
            ShipmentLoadTransshipmentKey = transshipmentKey,
        });
    }

    private Task<ShipmentLoad> LoadAsync(Guid key) =>
        _db.Context.ShipmentLoads.AsNoTracking().SingleAsync(x => x.Key == key);

    [Fact]
    public async Task Reverse_ReturnsTheBalanceToTheLoad()
    {
        var (load, transshipment, _, _) = await SeedThirdPartyEntryRegisteredAsync(outgoing: 30_000);

        await Service().ExecuteAsync(transshipment.Key!.Value, null, "tester");

        var saved = await LoadAsync(load.Key);
        Assert.Equal(decimal.Zero, saved.TransshippedQuantity);
        Assert.Equal(30_000m, saved.AvailableQuantity);

        Assert.Empty(await _db.Context.ShipmentLoadsTransshipments
            .AsNoTracking().Where(x => x.ShipmentLoadKey == load.Key).ToListAsync());
    }

    [Fact]
    public async Task Reverse_CancelsTheEntryAndItsReleases()
    {
        var (_, transshipment, entry, release) = await SeedThirdPartyEntryRegisteredAsync(outgoing: 30_000);

        await Service().ExecuteAsync(transshipment.Key!.Value, "armazem errado", "tester");

        var savedEntry = await _db.Context.StorageTransactions
            .AsNoTracking().SingleAsync(x => x.Key == entry.Key);
        Assert.Equal(StorageTransactionsStatus.Cancelled, savedEntry.TransactionStatus);

        var savedRelease = await _db.Context.ShipmentReleases
            .AsNoTracking().SingleAsync(x => x.Key == release.Key);
        Assert.Equal(ReleaseStatus.Cancelled, savedRelease.Status);
        Assert.Contains("armazem errado", savedRelease.CancellationReason);
    }

    [Fact]
    public async Task Reverse_OwnWarehouse_OnlyUnlinksTheReceipt()
    {
        var (load, transshipment, receipt) = await SeedOwnWarehouseEntryRegisteredAsync(outgoing: 30_000);

        await Service().ExecuteAsync(transshipment.Key!.Value, null, "tester");

        var savedReceipt = await _db.Context.StorageTransactions
            .AsNoTracking().SingleAsync(x => x.Key == receipt.Key);

        // O Receipt pertence à Entrada em Armazenagem: não pode ser cancelado por aqui.
        Assert.Equal(StorageTransactionsStatus.Confirmed, savedReceipt.TransactionStatus);
        Assert.Null(savedReceipt.ShipmentLoadTransshipmentKey);

        var saved = await LoadAsync(load.Key);
        Assert.Equal(30_000m, saved.AvailableQuantity);
    }

    [Fact]
    public async Task Reverse_RefusesWhenTheReleaseHasBeenConsumed()
    {
        // Consumo de VERDADE: ShippedQuantity > 0 na liberação gerada pela entrada — não um
        // valor que já nasceria zero de qualquer jeito.
        var (_, transshipment, _, _) = await SeedThirdPartyEntryRegisteredAsync(
            outgoing: 30_000, shippedQuantity: 5_000m);

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(transshipment.Key!.Value, null, "tester"));

        Assert.Contains("Estorne a Expedição de saída antes", error.Message);
    }

    [Fact]
    public async Task Reverse_RefusesWhenItIsNotTheLastOne()
    {
        var (load, first, _, _) = await SeedThirdPartyEntryRegisteredAsync(outgoing: 30_000, sequence: 1);

        // Um SEGUNDO transbordo de verdade, sequence maior — não um valor fabricado à parte.
        _db.Context.ShipmentLoadsTransshipments.Add(new ShipmentLoadTransshipment
        {
            ShipmentLoadKey = load.Key,
            Sequence = 2,
            WarehouseCode = TransshipmentWarehouse,
            OutgoingQuantity = 1_000m,
        });
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(first.Key!.Value, null, "tester"));

        Assert.Contains("transbordo 2", error.Message);
    }

    [Fact]
    public async Task Reverse_RefusesWhenAnExitIsAlreadyLinked()
    {
        var (load, transshipment, _, _) = await SeedThirdPartyEntryRegisteredAsync(outgoing: 30_000);

        // Saída de verdade vinculada ao transbordo (o que a Task 7 fará), não um estado que já
        // nasceria assim.
        LinkExit(load.Key, transshipment.Key!.Value);
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(transshipment.Key!.Value, null, "tester"));

        Assert.Contains("saída vinculada", error.Message);
    }

    [Fact]
    public async Task Reverse_RefusalOrigin_DoesNotUndoTheReturnInvoices()
    {
        var (load, transshipment, _, _) = await SeedThirdPartyEntryRegisteredAsync(
            outgoing: 30_000, origin: TransshipmentOrigin.Refusal);

        // A devolução da nota que originou a recusa: documento fiscal com ciclo próprio, alheio
        // ao transbordo.
        var returnDoc = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = "DEV0001",
            CardCode = CardCode,
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            WarehouseCode = OriginWarehouse,
            BranchCode = "01",
            GrossWeight = 30_000m,
            NetWeight = 30_000m,
            TransactionType = StorageTransactionType.SalesShipmentReturn,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
            RefusedFromShipmentLoadKey = load.Key,
        };
        _db.Context.StorageTransactions.Add(returnDoc);
        await _db.Context.SaveChangesAsync();

        await Service().ExecuteAsync(transshipment.Key!.Value, null, "tester");

        var savedReturnDoc = await _db.Context.StorageTransactions
            .AsNoTracking().SingleAsync(x => x.Key == returnDoc.Key);

        Assert.Equal(StorageTransactionsStatus.Confirmed, savedReturnDoc.TransactionStatus);
        Assert.Equal(load.Key, savedReturnDoc.RefusedFromShipmentLoadKey);
    }

    [Fact]
    public async Task Reverse_WithoutRegisteredEntry_JustRemovesTheRow()
    {
        var load = Load(30_000m);
        var transshipment = new ShipmentLoadTransshipment
        {
            ShipmentLoadKey = load.Key,
            Sequence = 1,
            WarehouseCode = TransshipmentWarehouse,
            WarehouseName = "ARMAZEM TERCEIRO",
            OutgoingQuantity = 30_000m,
        };
        _db.Context.ShipmentLoadsTransshipments.Add(transshipment);
        await _db.Context.SaveChangesAsync();

        await Service().ExecuteAsync(transshipment.Key!.Value, null, "tester");

        Assert.Empty(await _db.Context.ShipmentLoadsTransshipments
            .AsNoTracking().Where(x => x.ShipmentLoadKey == load.Key).ToListAsync());

        var saved = await LoadAsync(load.Key);
        Assert.Equal(30_000m, saved.AvailableQuantity);
    }

    [Fact]
    public async Task CancelStorageTransaction_RefusesTheTransshipmentEntry()
    {
        var (_, _, entry, _) = await SeedThirdPartyEntryRegisteredAsync(outgoing: 30_000);

        var service = new StorageTransactionsCancelService(
            _db, new ShipmentReleasesRecalculateShippedService(_db.Context));

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => service.ExecuteAsync(entry.Key, "tester"));

        Assert.Contains("Estornar Transbordo", error.Message);
    }

    [Fact]
    public async Task ReverseStorageTransaction_RefusesTheTransshipmentEntry()
    {
        var (_, _, entry, _) = await SeedThirdPartyEntryRegisteredAsync(outgoing: 30_000);

        var service = new StorageTransactionsReverseService(
            _db,
            new StorageAddressesGetBalanceService(null!),
            new ShipmentReleasesRecalculateShippedService(_db.Context),
            new FakeStringLocalizer<Resource>());

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => service.ExecuteAsync(entry.Key, "tester"));

        Assert.Contains("Estornar Transbordo", error.Message);
    }
}
