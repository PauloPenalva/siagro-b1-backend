using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// GAC-1171 (melhorias): marcar a carga como descarregada no destino, e desfazer. As duas ações
/// gravam só a marca, e o status resultante é o do recálculo.
/// </summary>
public class ShipmentLoadsDischargedServiceTests
{
    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();

    private ShipmentLoadsMarkDischargedService Mark() => new(
        _db, new ShipmentLoadsMovementLogService(_db.Context), new ShipmentLoadsChangeLogService(_db.Context));

    private ShipmentLoadsUndoDischargedService Undo() => new(
        _db, new ShipmentLoadsMovementLogService(_db.Context), new ShipmentLoadsChangeLogService(_db.Context));

    /// <summary>
    /// Carga coerente com o recálculo: 90 t montadas e uma nota confirmada de 90 t. O status e
    /// a marca semeados precisam casar com o que o recálculo derivaria.
    /// </summary>
    private async Task<ShipmentLoad> SeedAsync(
        ShipmentLoadStatus status = ShipmentLoadStatus.Invoiced,
        bool isDischarged = false,
        SalesInvoiceDeliveryStatus delivery = SalesInvoiceDeliveryStatus.Open,
        ShipmentLoadType loadType = ShipmentLoadType.Normal,
        decimal invoiced = 90_000)
    {
        var load = new ShipmentLoad
        {
            Key = Guid.NewGuid(),
            Code = "CG000041",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            LoadType = loadType,
            Status = status,
            IsDischarged = isDischarged,
            TotalQuantity = 90_000,
            InvoicedQuantity = invoiced,
        };
        _db.Context.ShipmentLoads.Add(load);

        var invoice = new SalesInvoice
        {
            Key = Guid.NewGuid(),
            CardCode = "C001",
            InvoiceNumber = "000000777",
            InvoiceStatus = InvoiceStatus.Confirmed,
            InvoiceType = SalesInvoiceType.Normal,
            ShipmentLoadKey = load.Key,
        };
        invoice.Items.Add(new SalesInvoiceItem
        {
            Key = Guid.NewGuid(),
            SalesInvoiceKey = invoice.Key,
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            Quantity = invoiced,
            DeliveredQuantity = delivery == SalesInvoiceDeliveryStatus.Closed ? invoiced : 0m,
            DeliveryStatus = delivery,
        });
        _db.Context.SalesInvoices.Add(invoice);

        await _db.Context.SaveChangesAsync();
        return load;
    }

    private Task<ShipmentLoad> SavedAsync() => _db.Context.ShipmentLoads.AsNoTracking().SingleAsync();

    [Fact]
    public async Task Marking_an_invoiced_load_makes_it_discharged_with_log_and_movement()
    {
        var load = await SeedAsync();

        await Mark().ExecuteAsync(load.Key, "tester");

        var saved = await SavedAsync();
        Assert.Equal(ShipmentLoadStatus.Discharged, saved.Status);
        Assert.True(saved.IsDischarged);
        Assert.Equal("tester", saved.UpdatedBy);

        var log = await _db.Context.ShipmentLoadsChangeLogs.SingleAsync();
        Assert.Equal(ShipmentLoadChangeLogFields.Status, log.Field);
        Assert.Equal("Faturada", log.OldValue);
        Assert.Equal("Descarregada", log.NewValue);

        var movement = await _db.Context.ShipmentLoadMovements.SingleAsync();
        Assert.Equal(ShipmentLoadMovementType.Discharged, movement.MovementType);
    }

    /// <summary>Review Focus 3: com a conferência toda encerrada, o resultado é Concluída.</summary>
    [Fact]
    public async Task Marking_with_every_delivery_closed_completes_the_load_and_logs_it()
    {
        var load = await SeedAsync(delivery: SalesInvoiceDeliveryStatus.Closed);

        await Mark().ExecuteAsync(load.Key, "tester");

        Assert.Equal(ShipmentLoadStatus.Completed, (await SavedAsync()).Status);
        Assert.Equal("Concluída", (await _db.Context.ShipmentLoadsChangeLogs.SingleAsync()).NewValue);
    }

    [Theory]
    [InlineData(ShipmentLoadStatus.Discharged, "já está marcada como descarregada")]
    [InlineData(ShipmentLoadStatus.Completed, "já foi concluída")]
    [InlineData(ShipmentLoadStatus.PartiallyInvoiced, "ainda tem saldo a faturar")]
    [InlineData(ShipmentLoadStatus.Cancelled, "está cancelada")]
    [InlineData(ShipmentLoadStatus.Returned, "foi devolvida ao armazém")]
    [InlineData(ShipmentLoadStatus.InTransshipment, "está em transbordo")]
    [InlineData(ShipmentLoadStatus.Open, "ainda não foi faturada")]
    [InlineData(ShipmentLoadStatus.Planned, "ainda não foi faturada")]
    public async Task Marking_outside_invoiced_is_refused(ShipmentLoadStatus status, string reason)
    {
        var load = await SeedAsync(status: status);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => Mark().ExecuteAsync(load.Key, "tester"));

        Assert.Equal($"A carga CG000041 {reason}.", ex.Message);
        Assert.Empty(_db.Context.ShipmentLoadsChangeLogs);
    }

    /// <summary>
    /// A trava de entrada lê o status PERSISTIDO, e ele pode estar defasado. Aqui a carga diz
    /// Faturada, mas as notas cobrem só parte do total. Quem decide é o status recalculado: fora
    /// de Descarregada e Concluída, a ação é recusada e nada é gravado.
    /// </summary>
    [Fact]
    public async Task Marking_a_stale_invoiced_load_that_recalculates_out_of_invoiced_is_refused()
    {
        var load = await SeedAsync(invoiced: 50_000);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => Mark().ExecuteAsync(load.Key, "tester"));

        Assert.Equal("A carga CG000041 não está faturada: recalcule o saldo da carga.", ex.Message);
        var saved = await SavedAsync();
        Assert.False(saved.IsDischarged);
        Assert.Equal(ShipmentLoadStatus.Invoiced, saved.Status);
        Assert.Empty(_db.Context.ShipmentLoadsChangeLogs);
        Assert.Empty(_db.Context.ShipmentLoadMovements);
    }

    [Fact]
    public async Task Marking_a_removal_load_is_refused()
    {
        var load = await SeedAsync(loadType: ShipmentLoadType.Removal);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => Mark().ExecuteAsync(load.Key, "tester"));

        Assert.Contains("Remoção", ex.Message);
    }

    [Fact]
    public async Task Marking_an_unknown_load_is_not_found()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => Mark().ExecuteAsync(Guid.NewGuid(), "tester"));
    }

    [Fact]
    public async Task Undoing_returns_the_load_to_invoiced_with_log_and_movement()
    {
        var load = await SeedAsync(status: ShipmentLoadStatus.Discharged, isDischarged: true);

        await Undo().ExecuteAsync(load.Key, "tester");

        var saved = await SavedAsync();
        Assert.Equal(ShipmentLoadStatus.Invoiced, saved.Status);
        Assert.False(saved.IsDischarged);

        var log = await _db.Context.ShipmentLoadsChangeLogs.SingleAsync();
        Assert.Equal("Descarregada", log.OldValue);
        Assert.Equal("Faturada", log.NewValue);

        var movement = await _db.Context.ShipmentLoadMovements.SingleAsync();
        Assert.Equal(ShipmentLoadMovementType.DischargeUndone, movement.MovementType);
    }

    [Fact]
    public async Task Undoing_a_completed_load_is_refused_pointing_to_the_reconciliation()
    {
        var load = await SeedAsync(
            status: ShipmentLoadStatus.Completed, isDischarged: true, delivery: SalesInvoiceDeliveryStatus.Closed);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => Undo().ExecuteAsync(load.Key, "tester"));

        Assert.Equal(
            "A carga CG000041 está concluída. Estorne a conferência de entrega antes de desfazer a descarga.",
            ex.Message);
        Assert.True((await SavedAsync()).IsDischarged);
    }

    [Fact]
    public async Task Undoing_a_load_that_is_not_discharged_is_refused()
    {
        var load = await SeedAsync();

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => Undo().ExecuteAsync(load.Key, "tester"));

        Assert.Equal("A carga CG000041 não está marcada como descarregada.", ex.Message);
    }
}
