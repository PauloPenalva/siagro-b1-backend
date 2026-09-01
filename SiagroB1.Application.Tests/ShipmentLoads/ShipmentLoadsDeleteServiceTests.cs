using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// Exclusão da carga criada por engano — permitida SÓ no planejamento.
/// </summary>
/// <remarks>
/// A permissão estreita é o ponto destes testes: em qualquer outro caso a resposta é cancelar
/// a carga, que preserva o rastro. Se alguém afrouxar isso, é aqui que aparece.
/// </remarks>
public class ShipmentLoadsDeleteServiceTests
{
    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();

    private ShipmentLoadsDeleteService Service() => new(_db);

    private ShipmentLoad Load(ShipmentLoadStatus status = ShipmentLoadStatus.Planned)
    {
        var load = new ShipmentLoad
        {
            Key = Guid.NewGuid(),
            Code = "CG000001",
            BranchCode = "01",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            TruckCode = "ABC1D23",
            WarehouseCode = "ARM01",
            Status = status,
        };

        _db.Context.ShipmentLoads.Add(load);
        return load;
    }

    [Fact]
    public async Task Deletes_a_planned_load_and_its_movements()
    {
        var load = Load();
        _db.Context.ShipmentLoadMovements.Add(new ShipmentLoadMovement
        {
            Key = Guid.NewGuid(),
            ShipmentLoadKey = load.Key,
            MovementType = ShipmentLoadMovementType.Planned,
            Description = "Carga planejada.",
        });
        await _db.Context.SaveChangesAsync();

        await Service().ExecuteAsync(load.Key);

        Assert.Empty(_db.Context.ShipmentLoads);
        // Os movimentos têm FK real e todas as FKs deste projeto são NoAction: sem removê-los
        // primeiro, o delete do pai quebraria.
        Assert.Empty(_db.Context.ShipmentLoadMovements);
    }

    [Theory]
    [InlineData(ShipmentLoadStatus.Open)]
    [InlineData(ShipmentLoadStatus.PartiallyInvoiced)]
    [InlineData(ShipmentLoadStatus.Invoiced)]
    [InlineData(ShipmentLoadStatus.Cancelled)]
    public async Task Refuses_to_delete_a_load_that_left_planning(ShipmentLoadStatus status)
    {
        var load = Load(status);
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(load.Key));

        Assert.Contains("cancele", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(_db.Context.ShipmentLoads);
    }

    /// <summary>
    /// As três condições são verificadas separadamente, não deduzidas uma da outra: uma carga
    /// pode estar <c>Planned</c> por drift e ainda ter romaneio.
    /// </summary>
    [Fact]
    public async Task Refuses_to_delete_a_planned_load_that_still_has_a_shipment()
    {
        var load = Load();
        _db.Context.StorageTransactions.Add(new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = "R1",
            CardCode = "C001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            WarehouseCode = "ARM01",
            BranchCode = "01",
            TruckCode = "ABC1D23",
            TransactionType = StorageTransactionType.SalesShipment,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
            ShipmentLoadKey = load.Key,
        });
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(load.Key));

        Assert.Contains("romaneio", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(_db.Context.ShipmentLoads);
    }

    [Fact]
    public async Task Refuses_to_delete_a_planned_load_that_has_an_invoice()
    {
        var load = Load();
        _db.Context.SalesInvoices.Add(new SalesInvoice
        {
            Key = Guid.NewGuid(),
            InvoiceNumber = "000001",
            ShipmentLoadKey = load.Key,
            InvoiceStatus = InvoiceStatus.Cancelled,
            InvoiceType = SalesInvoiceType.Normal,
            CardCode = "C001",
        });
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(load.Key));

        Assert.Contains("documento", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(_db.Context.ShipmentLoads);
    }
}
