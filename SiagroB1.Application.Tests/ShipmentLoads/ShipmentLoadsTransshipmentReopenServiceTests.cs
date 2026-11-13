using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using static SiagroB1.Application.Tests.ShipmentLoads.ShipmentLoadsTransferTestData;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// Reabrir a transferência (GAC-1176): o desfazer manual da conclusão manual — sem ele o engano
/// de um clique não teria volta, porque cancelar/excluir a carga com transbordo é recusado.
/// </summary>
public class ShipmentLoadsTransshipmentReopenServiceTests
{
    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();

    private ShipmentLoadsTransshipmentCompleteService CompleteService() => new(
        _db,
        new ShipmentLoadsMovementLogService(_db.Context),
        new ShipmentLoadsChangeLogService(_db.Context));

    private ShipmentLoadsTransshipmentReopenService Service() => new(
        _db,
        new ShipmentLoadsMovementLogService(_db.Context),
        new ShipmentLoadsChangeLogService(_db.Context));

    private async Task<(ShipmentLoad Load, ShipmentLoadTransshipment Transshipment, ShipmentRelease Release)>
        SeedCompletedAsync(TransshipmentType type = TransshipmentType.Transfer)
    {
        var load = Load(_db);
        var transshipment = await TransshipmentAsync(_db, load, type: type);
        var (_, release) = await RegisterThirdPartyEntryAsync(_db, transshipment);
        await CompleteService().ExecuteAsync(transshipment.Key!.Value, "tester");
        return (load, transshipment, release);
    }

    [Fact]
    public async Task Reopen_PutsTheLoadBackInTransit()
    {
        var (load, transshipment, _) = await SeedCompletedAsync();

        await Service().ExecuteAsync(transshipment.Key!.Value, "tester");

        var saved = await _db.Context.ShipmentLoads.AsNoTracking().SingleAsync(x => x.Key == load.Key);
        Assert.Equal(ShipmentLoadStatus.InTransshipment, saved.Status);
        Assert.True(saved.IsInTransfer);

        var savedTransshipment = await _db.Context.ShipmentLoadsTransshipments.AsNoTracking()
            .SingleAsync(x => x.Key == transshipment.Key);
        Assert.Null(savedTransshipment.FinalDestinationAt);
        Assert.Null(savedTransshipment.FinalDestinationBy);

        Assert.True(await _db.Context.ShipmentLoadMovements.AsNoTracking().AnyAsync(x =>
            x.ShipmentLoadKey == load.Key && x.MovementType == ShipmentLoadMovementType.TransferReopened));
    }

    /// <summary>Reabrir não desfaz a conversão: a parada continua sendo uma transferência.</summary>
    [Fact]
    public async Task Reopen_KeepsTheTransferType()
    {
        var (_, transshipment, _) = await SeedCompletedAsync(TransshipmentType.Transshipment);

        await Service().ExecuteAsync(transshipment.Key!.Value, "tester");

        Assert.Equal(
            TransshipmentType.Transfer,
            (await _db.Context.ShipmentLoadsTransshipments.AsNoTracking()
                .SingleAsync(x => x.Key == transshipment.Key)).Type);
    }

    [Fact]
    public async Task Reopen_RefusesATransshipmentThatWasNotCompleted()
    {
        var load = Load(_db);
        var transshipment = await TransshipmentAsync(_db, load);
        await RegisterThirdPartyEntryAsync(_db, transshipment);

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(transshipment.Key!.Value, "tester"));

        Assert.Contains("não foi concluída", ex.Message);
    }

    /// <summary>
    /// A mercadoria já saiu do armazém de destino noutra carga: reabrir deixaria esta carga "em
    /// transbordo" sem estoque e sem estorno possível.
    /// </summary>
    [Fact]
    public async Task Reopen_RefusesWhenTheReleaseWasAlreadyShipped()
    {
        var (load, transshipment, release) = await SeedCompletedAsync();
        release.ShippedQuantity = 10_000m;
        await _db.Context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(transshipment.Key!.Value, "tester"));

        Assert.Contains("já foi embarcada", ex.Message);
        Assert.Equal(
            ShipmentLoadStatus.Completed,
            (await _db.Context.ShipmentLoads.AsNoTracking().SingleAsync(x => x.Key == load.Key)).Status);
    }

    [Fact]
    public async Task Reopen_RefusesATransshipmentThatIsNotTheLast()
    {
        var (load, transshipment, _) = await SeedCompletedAsync();
        await TransshipmentAsync(_db, load, sequence: 2);

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(transshipment.Key!.Value, "tester"));

        Assert.Contains("último", ex.Message);
    }

    [Fact]
    public async Task Reopen_RefusesACancelledLoad()
    {
        var (load, transshipment, _) = await SeedCompletedAsync();
        load.Status = ShipmentLoadStatus.Cancelled;
        await _db.Context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(transshipment.Key!.Value, "tester"));

        Assert.Contains("cancelada", ex.Message);
    }
}
