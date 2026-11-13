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
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// GAC-1176, cenário do chamado: a carga carrega num armazém de terceiro, descarrega em OUTRO
/// armazém de terceiro e termina ali — nunca "bateu" no cliente, nunca foi faturada.
/// </summary>
/// <remarks>
/// Pelos serviços reais e na ordem em que a tela os chama: vincular a origem, iniciar a
/// transferência, registrar a entrada pesada e concluir. Prova o que os testes por serviço não
/// provam juntos — que a mercadoria fica disponível no destino (liberação viva) e que tirá-la de
/// lá depois, em OUTRA carga, não mexe na carga concluída.
/// </remarks>
public class ShipmentLoadsThirdPartyTransferScenarioTests
{
    private const string Origin = "BEATRICE";
    private const string Destination = "CAPAL";
    private const string Truck = "ABC1D23";

    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private static FakeWarehouseService Warehouses() =>
        new(new Dictionary<string, string> { [Origin] = "BEATRICE", [Destination] = "CAPAL" });

    private ShipmentLoadsMovementLogService MovementLog() => new(_db.Context);
    private ShipmentLoadsChangeLogService ChangeLog() => new(_db.Context);

    private StorageTransactionsCreateService StorageCreate() =>
        new(_db,
            new FakeDocNumberSequenceService(),
            new FakeBusinessPartnerService(names: new Dictionary<string, string> { ["C0001"] = "CLIENTE" }),
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

    private ShipmentLoadsAttachTransactionsService Attach() => new(_db, MovementLog());

    private ShipmentLoadsTransshipmentStartService Start() =>
        new(_db, Warehouses(), MovementLog(), new WarehouseComplementService(_db));

    private ShipmentLoadsTransshipmentRegisterEntryService RegisterEntry() => new(
        _db,
        Warehouses(),
        new WarehouseComplementService(_db),
        StorageCreate(),
        StorageConfirm(),
        new ShipmentReleasesFromReturnService(_db.Context),
        MovementLog());

    private ShipmentLoadsTransshipmentCompleteService Complete() => new(_db, MovementLog(), ChangeLog());
    private ShipmentLoadsTransshipmentReopenService Reopen() => new(_db, MovementLog(), ChangeLog());
    private ShipmentLoadsTransshipmentReverseService Reverse() => new(_db, MovementLog());

    private ShipmentLoad Load(string code, string warehouse)
    {
        var load = new ShipmentLoad
        {
            Key = Guid.NewGuid(),
            Code = code,
            BranchCode = "01",
            ItemCode = "SOJA",
            ItemName = "SOJA EM GRAOS",
            UnitOfMeasureCode = "KG",
            TruckCode = Truck,
            WarehouseCode = warehouse,
            Status = ShipmentLoadStatus.Planned,
        };
        _db.Context.ShipmentLoads.Add(load);
        return load;
    }

    private ShipmentRelease OriginRelease()
    {
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(),
            Code = "PC-BEATRICE",
            CardCode = "F0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "2026",
            DeliveryLocationCode = Origin,
            BranchCode = "01",
            Status = ContractStatus.Approved,
            TotalVolume = 1_000_000m,
        };
        var release = new ShipmentRelease
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = contract.Key,
            DeliveryLocationCode = Origin,
            ReleasedQuantity = 1_000_000m,
            Status = ReleaseStatus.Actived,
        };
        _db.Context.PurchaseContracts.Add(contract);
        _db.Context.ShipmentReleases.Add(release);
        return release;
    }

    private StorageTransaction Shipment(string code, string warehouse, decimal weight, Guid? releaseKey)
    {
        var shipment = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = code,
            CardCode = "C0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            WarehouseCode = warehouse,
            BranchCode = "01",
            TruckCode = Truck,
            GrossWeight = weight,
            NetWeight = weight,
            TransactionType = StorageTransactionType.SalesShipment,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
            ShipmentReleaseKey = releaseKey,
        };
        _db.Context.StorageTransactions.Add(shipment);
        return shipment;
    }

    private Task<ShipmentLoad> SavedLoadAsync(Guid key) =>
        _db.Context.ShipmentLoads.AsNoTracking().SingleAsync(x => x.Key == key);

    /// <summary>Carga de 30 t transferida da Beatrice para a Capal, com 200 kg de quebra, já concluída.</summary>
    private async Task<(ShipmentLoad Load, ShipmentLoadTransshipment Transfer, StorageTransaction OriginShipment)>
        TransferAndCompleteAsync()
    {
        var load = Load("CG000001", Origin);
        var originRelease = OriginRelease();
        var originShipment = Shipment("R0001", Origin, 30_000m, originRelease.Key);
        await _db.SaveChangesAsync();

        await Attach().ExecuteAsync(load.Key, [originShipment.Key], null, "tester");

        var transfer = await Start().ExecuteAsync(
            load.Key, Destination, DateTime.Today, null, "tester", TransshipmentType.Transfer);
        await RegisterEntry().ExecuteAsync(transfer.Key!.Value, 29_800m, DateTime.Today, null, "tester");

        Assert.True((await SavedLoadAsync(load.Key)).IsInTransfer);

        await Complete().ExecuteAsync(transfer.Key!.Value, "tester");

        return (load, transfer, originShipment);
    }

    [Fact]
    public async Task A_load_transferred_between_third_party_warehouses_completes_without_invoicing()
    {
        var (load, transfer, originShipment) = await TransferAndCompleteAsync();

        var saved = await SavedLoadAsync(load.Key);
        Assert.Equal(ShipmentLoadStatus.Completed, saved.Status);
        Assert.Equal(30_000m, saved.TotalQuantity);
        Assert.Equal(30_000m, saved.TransshippedQuantity);
        Assert.Equal(decimal.Zero, saved.InvoicedQuantity);
        Assert.Equal(decimal.Zero, saved.AvailableQuantity);
        Assert.False(saved.IsInTransfer);

        // As etapas do transporte e a quebra ficam na linha: saiu 30 t, entrou 29,8 t.
        var savedTransfer = await _db.Context.ShipmentLoadsTransshipments.AsNoTracking()
            .SingleAsync(x => x.Key == transfer.Key);
        Assert.Equal(30_000m, savedTransfer.OutgoingQuantity);
        Assert.Equal(29_800m, savedTransfer.EntryQuantity);
        Assert.Equal(200m, savedTransfer.ShrinkageQuantity);

        // O romaneio de origem sai da Montagem: a mercadoria foi embora, não pode ir para outra carga.
        Assert.Equal(
            StorageTransactionsStatus.Invoiced,
            (await _db.Context.StorageTransactions.AsNoTracking().SingleAsync(x => x.Key == originShipment.Key))
            .TransactionStatus);

        // A mercadoria está disponível na Capal: liberação viva, pelo peso que ENTROU.
        var destinationRelease = await _db.Context.ShipmentReleases.AsNoTracking()
            .SingleAsync(x => x.Origin == ReleaseOrigin.Transshipment);
        Assert.Equal(ReleaseStatus.Actived, destinationRelease.Status);
        Assert.Equal(Destination, destinationRelease.DeliveryLocationCode);
        Assert.Equal(29_800m, destinationRelease.ReleasedQuantity);

        var movements = await _db.Context.ShipmentLoadMovements.AsNoTracking()
            .Where(x => x.ShipmentLoadKey == load.Key)
            .Select(x => x.MovementType)
            .ToListAsync();
        Assert.Contains(ShipmentLoadMovementType.TransshipmentStarted, movements);
        Assert.Contains(ShipmentLoadMovementType.TransshipmentEntered, movements);
        Assert.Contains(ShipmentLoadMovementType.TransferCompleted, movements);
    }

    /// <summary>
    /// Depois, a mercadoria sai da Capal para um cliente em OUTRA carga, consumindo a liberação da
    /// transferência. A carga concluída não é tocada — e a partir daí não pode mais ser reaberta.
    /// </summary>
    [Fact]
    public async Task Shipping_the_goods_out_of_the_destination_in_another_load_does_not_touch_the_completed_one()
    {
        var (load, transfer, _) = await TransferAndCompleteAsync();

        var destinationRelease = await _db.Context.ShipmentReleases
            .SingleAsync(x => x.Origin == ReleaseOrigin.Transshipment);

        var secondLoad = Load("CG000002", Destination);
        var exit = Shipment("R0002", Destination, 29_800m, destinationRelease.Key);
        destinationRelease.ShippedQuantity = 29_800m;
        await _db.SaveChangesAsync();

        await Attach().ExecuteAsync(secondLoad.Key, [exit.Key], null, "tester");

        Assert.Equal(ShipmentLoadStatus.Open, (await SavedLoadAsync(secondLoad.Key)).Status);

        var first = await SavedLoadAsync(load.Key);
        Assert.Equal(ShipmentLoadStatus.Completed, first.Status);
        Assert.Equal(30_000m, first.TotalQuantity);

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => Reopen().ExecuteAsync(transfer.Key!.Value, "tester"));
        Assert.Contains("já foi embarcada", ex.Message);
    }

    /// <summary>O engano tem volta inteira: reabrir e estornar devolvem o volume à carga.</summary>
    [Fact]
    public async Task Reopening_and_reversing_gives_the_volume_back_to_the_load()
    {
        var (load, transfer, originShipment) = await TransferAndCompleteAsync();

        await Reopen().ExecuteAsync(transfer.Key!.Value, "tester");
        Assert.Equal(ShipmentLoadStatus.InTransshipment, (await SavedLoadAsync(load.Key)).Status);

        await Reverse().ExecuteAsync(transfer.Key!.Value, "armazém errado", "tester");

        var saved = await SavedLoadAsync(load.Key);
        Assert.Equal(ShipmentLoadStatus.Open, saved.Status);
        Assert.Equal(30_000m, saved.AvailableQuantity);
        Assert.False(saved.IsInTransfer);

        Assert.Equal(
            StorageTransactionsStatus.Confirmed,
            (await _db.Context.StorageTransactions.AsNoTracking().SingleAsync(x => x.Key == originShipment.Key))
            .TransactionStatus);
        Assert.Equal(
            ReleaseStatus.Cancelled,
            (await _db.Context.ShipmentReleases.AsNoTracking().SingleAsync(x => x.Origin == ReleaseOrigin.Transshipment))
            .Status);
    }
}
