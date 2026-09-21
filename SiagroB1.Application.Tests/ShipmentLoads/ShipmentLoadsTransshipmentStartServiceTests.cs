using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// Iniciar o transbordo (GAC-1181, Task 4): descarrega o saldo INTEIRO disponível da carga num
/// armazém intermediário. Registrar a entrada é a Task 5, estornar é a Task 6 — nenhuma das duas
/// é exercida aqui.
/// </summary>
public class ShipmentLoadsTransshipmentStartServiceTests
{
    private const string Warehouse = "ARM99";

    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();

    private static FakeWarehouseService Warehouses() =>
        new(new Dictionary<string, string> { [Warehouse] = "ARMAZEM TERCEIRO" });

    private ShipmentLoadsTransshipmentStartService Service() => new(
        _db,
        Warehouses(),
        new ShipmentLoadsMovementLogService(_db.Context));

    private ShipmentLoad Load(
        ShipmentLoadStatus status = ShipmentLoadStatus.Open,
        decimal totalQuantity = 30_000,
        ShipmentLoadType loadType = ShipmentLoadType.Normal)
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
            LoadType = loadType,
            Status = status,
            TotalQuantity = totalQuantity,
        };

        _db.Context.ShipmentLoads.Add(load);
        return load;
    }

    /// <summary>Consome o saldo inteiro com uma nota confirmada, para simular carga sem saldo.</summary>
    private void FullyInvoice(ShipmentLoad load, decimal quantity)
    {
        var invoice = new SalesInvoice
        {
            Key = Guid.NewGuid(),
            CardCode = "C001",
            InvoiceStatus = InvoiceStatus.Confirmed,
            InvoiceType = SalesInvoiceType.Normal,
            ShipmentLoadKey = load.Key,
        };

        invoice.AddItem(new SalesInvoiceItem
        {
            Key = Guid.NewGuid(),
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            Quantity = quantity,
        });

        _db.Context.SalesInvoices.Add(invoice);
    }

    /// <summary>Romaneio de reembarque que fecha um transbordo (o que a Task 5 fará de verdade).</summary>
    private StorageTransaction Reload(Guid loadKey, Guid transshipmentKey, decimal grossWeight)
    {
        var shipment = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = "R-RECARGA",
            CardCode = "C001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            WarehouseCode = Warehouse,
            BranchCode = "01",
            GrossWeight = grossWeight,
            NetWeight = grossWeight,
            TransactionType = StorageTransactionType.SalesShipment,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
            ShipmentLoadKey = loadKey,
            ShipmentLoadTransshipmentKey = transshipmentKey,
        };

        _db.Context.StorageTransactions.Add(shipment);
        return shipment;
    }

    private Task<ShipmentLoad> LoadAsync(Guid key) =>
        _db.Context.ShipmentLoads.AsNoTracking().SingleAsync(x => x.Key == key);

    [Fact]
    public async Task Start_TakesTheWholeAvailableBalance()
    {
        var load = Load(totalQuantity: 30_000);
        await _db.Context.SaveChangesAsync();

        var transshipment = await Service().ExecuteAsync(
            load.Key, Warehouse, DateTime.Today, null, "tester");

        Assert.Equal(30_000m, transshipment.OutgoingQuantity);
        Assert.Equal(decimal.Zero, transshipment.EntryQuantity);
    }

    [Fact]
    public async Task Start_LeavesTheLoadInTransshipment()
    {
        var load = Load(totalQuantity: 30_000);
        await _db.Context.SaveChangesAsync();

        await Service().ExecuteAsync(load.Key, Warehouse, DateTime.Today, null, "tester");

        var saved = await LoadAsync(load.Key);
        Assert.Equal(ShipmentLoadStatus.InTransshipment, saved.Status);
    }

    /// <summary>
    /// O segundo transbordo só numera 2 depois que o primeiro é FECHADO (reembarque vinculado) —
    /// senão a trava "não empilha transbordo sobre transbordo aberto" recusaria antes.
    /// </summary>
    [Fact]
    public async Task Start_NumbersTheSequence()
    {
        var load = Load(totalQuantity: 30_000);
        await _db.Context.SaveChangesAsync();

        var first = await Service().ExecuteAsync(load.Key, Warehouse, DateTime.Today, null, "tester");
        Assert.Equal(1, first.Sequence);

        // Fecha o primeiro transbordo com o reembarque (o que a Task 5 fará de verdade) e soma o
        // peso recarregado ao total, como qualquer romaneio novo vinculado à carga faria.
        Reload(load.Key, first.Key!.Value, 30_000);
        load.TotalQuantity += 30_000m;
        await _db.Context.SaveChangesAsync();

        var second = await Service().ExecuteAsync(load.Key, Warehouse, DateTime.Today, null, "tester");
        Assert.Equal(2, second.Sequence);
    }

    [Fact]
    public async Task Start_RefusesWhenThereIsNoBalance()
    {
        var load = Load(totalQuantity: 30_000);
        FullyInvoice(load, 30_000);
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(load.Key, Warehouse, DateTime.Today, null, "tester"));

        Assert.Contains("CG000001", error.Message);
    }

    [Theory]
    [InlineData(ShipmentLoadStatus.Cancelled, ShipmentLoadType.Normal)]
    [InlineData(ShipmentLoadStatus.Returned, ShipmentLoadType.Normal)]
    [InlineData(ShipmentLoadStatus.Open, ShipmentLoadType.Removal)]
    public async Task Start_RefusesCancelledOrReturnedOrRemovalLoad(
        ShipmentLoadStatus status, ShipmentLoadType loadType)
    {
        var load = Load(status: status, totalQuantity: 30_000, loadType: loadType);
        await _db.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(load.Key, Warehouse, DateTime.Today, null, "tester"));
    }

    /// <summary>
    /// A trava load-bearing (decisão do GAC-1181): mesmo havendo saldo recalculado disponível
    /// (ex.: mais romaneio vinculado depois), um transbordo ainda aberto recusa o próximo — senão
    /// o quarto termo do saldo perderia o mais antigo em silêncio.
    /// </summary>
    [Fact]
    public async Task Start_RefusesWhenTheLastTransshipmentIsOpen()
    {
        var load = Load(totalQuantity: 60_000);
        _db.Context.ShipmentLoadsTransshipments.Add(new ShipmentLoadTransshipment
        {
            ShipmentLoadKey = load.Key,
            Sequence = 1,
            WarehouseCode = Warehouse,
            OutgoingQuantity = 30_000,
        });
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(load.Key, Warehouse, DateTime.Today, null, "tester"));

        Assert.Contains("CG000001", error.Message);
    }

    [Fact]
    public async Task Start_LogsTheMovement()
    {
        var load = Load(totalQuantity: 30_000);
        await _db.Context.SaveChangesAsync();

        await Service().ExecuteAsync(load.Key, Warehouse, DateTime.Today, null, "tester");

        var movement = await _db.Context.ShipmentLoadMovements
            .SingleAsync(x => x.ShipmentLoadKey == load.Key);

        Assert.Equal(ShipmentLoadMovementType.TransshipmentStarted, movement.MovementType);
        // Assinada: sai volume da carga, então é negativa.
        Assert.Equal(-30_000m, movement.Quantity);
        Assert.Contains(Warehouse, movement.Description);
    }
}
