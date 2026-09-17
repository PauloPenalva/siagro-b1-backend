using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using static SiagroB1.Application.Tests.ShipmentLoads.ShipmentLoadsRemovalTestData;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// Conclusão e reabertura da carga de remoção (GAC-1175).
/// </summary>
/// <remarks>
/// <c>Completed</c> é a única situação da carga gravada por ação direta do usuário além de
/// <c>Cancelled</c> — o recálculo não tem como derivá-la, porque "a remoção acabou" não é um
/// número: uma carga pode receber recebimentos ao longo do dia e só depois fechar.
/// </remarks>
public class ShipmentLoadsCompleteServiceTests
{
    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();

    private ShipmentLoadsCompleteService CompleteService() => new(
        _db,
        new ShipmentLoadsMovementLogService(_db.Context),
        new ShipmentLoadsChangeLogService(_db.Context));

    private ShipmentLoadsReopenService ReopenService() => new(
        _db,
        new ShipmentLoadsMovementLogService(_db.Context),
        new ShipmentLoadsChangeLogService(_db.Context));

    private ShipmentLoad NormalLoad(ShipmentLoadStatus status = ShipmentLoadStatus.Open)
    {
        var load = new ShipmentLoad
        {
            Key = Guid.NewGuid(),
            Code = "CG000002",
            BranchCode = "01",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            TruckCode = "ABC1D23",
            WarehouseCode = "ARM01",
            LoadType = ShipmentLoadType.Normal,
            Status = status,
            TotalQuantity = 30_000,
        };

        _db.Context.ShipmentLoads.Add(load);
        return load;
    }

    [Fact]
    public async Task Completing_a_loaded_removal_marks_it_completed()
    {
        var load = RemovalLoad(_db, ShipmentLoadStatus.Open, totalQuantity: 30_000);
        Receipt(_db, "E1", 30_000, shipmentLoadKey: load.Key);
        await _db.Context.SaveChangesAsync();

        await CompleteService().ExecuteAsync(load.Key, "tester");

        var saved = await _db.Context.ShipmentLoads.SingleAsync();
        Assert.Equal(ShipmentLoadStatus.Completed, saved.Status);
    }

    [Fact]
    public async Task Completing_logs_the_status_change_and_a_movement()
    {
        var load = RemovalLoad(_db, ShipmentLoadStatus.Open, totalQuantity: 30_000);
        Receipt(_db, "E1", 30_000, shipmentLoadKey: load.Key);
        await _db.Context.SaveChangesAsync();

        await CompleteService().ExecuteAsync(load.Key, "tester");

        var log = await _db.Context.ShipmentLoadsChangeLogs.SingleAsync();
        Assert.Equal(ShipmentLoadChangeLogFields.Status, log.Field);
        Assert.Equal("Carregada", log.OldValue);
        Assert.Equal("Concluída", log.NewValue);

        var movement = await _db.Context.ShipmentLoadMovements.SingleAsync();
        Assert.Equal(ShipmentLoadMovementType.Completed, movement.MovementType);
    }

    [Fact]
    public async Task Completing_a_planned_removal_is_refused()
    {
        var load = RemovalLoad(_db);
        await _db.Context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => CompleteService().ExecuteAsync(load.Key, "tester"));

        Assert.Contains("romaneio", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Completing_a_normal_load_is_refused()
    {
        var load = NormalLoad();
        await _db.Context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => CompleteService().ExecuteAsync(load.Key, "tester"));

        Assert.Contains("remoção", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Completing_twice_is_refused()
    {
        var load = RemovalLoad(_db, ShipmentLoadStatus.Completed, totalQuantity: 30_000);
        await _db.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<ApplicationException>(
            () => CompleteService().ExecuteAsync(load.Key, "tester"));
    }

    [Fact]
    public async Task Reopening_returns_the_load_to_the_recalculated_status()
    {
        var load = RemovalLoad(_db, ShipmentLoadStatus.Completed, totalQuantity: 30_000);
        Receipt(_db, "E1", 30_000, shipmentLoadKey: load.Key);
        await _db.Context.SaveChangesAsync();

        await ReopenService().ExecuteAsync(load.Key, "tester");

        var saved = await _db.Context.ShipmentLoads.SingleAsync();
        Assert.Equal(ShipmentLoadStatus.Open, saved.Status);

        var movement = await _db.Context.ShipmentLoadMovements.SingleAsync();
        Assert.Equal(ShipmentLoadMovementType.Reopened, movement.MovementType);
    }

    /// <summary>
    /// Reabrir uma carga cujos romaneios foram todos desvinculados antes de concluir devolve
    /// <c>Planned</c>, e não <c>Open</c>: o status vem do recálculo, não de um valor fixo.
    /// </summary>
    [Fact]
    public async Task Reopening_an_empty_load_returns_it_to_planned()
    {
        var load = RemovalLoad(_db, ShipmentLoadStatus.Completed);
        await _db.Context.SaveChangesAsync();

        await ReopenService().ExecuteAsync(load.Key, "tester");

        var saved = await _db.Context.ShipmentLoads.SingleAsync();
        Assert.Equal(ShipmentLoadStatus.Planned, saved.Status);
    }

    [Fact]
    public async Task Reopening_a_load_that_is_not_completed_is_refused()
    {
        var load = RemovalLoad(_db, ShipmentLoadStatus.Open, totalQuantity: 30_000);
        await _db.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<ApplicationException>(
            () => ReopenService().ExecuteAsync(load.Key, "tester"));
    }
}
