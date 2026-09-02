using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// Cancelamento da carga. A trava é pelas NOTAS ligadas, não pelo status da carga: uma carga
/// parcialmente faturada continua Open/PartiallyInvoiced e o status sozinho não protegeria nada.
/// </summary>
public class ShipmentLoadsCancelServiceTests
{
    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();

    private ShipmentLoadsCancelService Service() => new(
        _db,
        new ShipmentLoadsCompositionGuardService(_db.Context),
        new ShipmentLoadsMovementLogService(_db.Context));

    private ShipmentLoad Load(ShipmentLoadStatus status = ShipmentLoadStatus.Open)
    {
        var load = new ShipmentLoad
        {
            Key = Guid.NewGuid(),
            Code = "CG000007",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            TruckCode = "ABC1D23",
            BranchCode = "01",
            TotalQuantity = 90_000,
            Status = status,
        };

        _db.Context.ShipmentLoads.Add(load);
        return load;
    }

    private StorageTransaction Shipment(ShipmentLoad load, string code)
    {
        var transaction = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = code,
            CardCode = "C001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            WarehouseCode = "ARM01",
            BranchCode = "01",
            TruckCode = "ABC1D23",
            GrossWeight = 30_000,
            TransactionType = StorageTransactionType.SalesShipment,
            TransactionStatus = StorageTransactionsStatus.Invoiced,
            ShipmentLoadKey = load.Key,
        };

        _db.Context.StorageTransactions.Add(transaction);
        return transaction;
    }

    /// <summary>
    /// A nota nasce COM item: a trava decide pelo volume consumido, e uma nota sem linha não
    /// consome nada. Um documento sem item nunca existe no fluxo real.
    /// </summary>
    private SalesInvoice Invoice(
        ShipmentLoad load,
        InvoiceStatus status,
        SalesInvoiceType type = SalesInvoiceType.Normal,
        decimal quantity = 30_000m,
        Guid? originKey = null,
        string invoiceNumber = "000000123")
    {
        var invoice = new SalesInvoice
        {
            Key = Guid.NewGuid(),
            CardCode = "C001",
            InvoiceNumber = invoiceNumber,
            InvoiceStatus = status,
            InvoiceType = type,
            SalesInvoiceOriginKey = originKey,
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
        return invoice;
    }

    /// <summary>
    /// Volume ainda consumido trava a composição — inclusive em <c>Returned</c>, porque a origem
    /// retornada continua consumindo até a DEVOLUÇÃO ser confirmada.
    /// </summary>
    [Theory]
    [InlineData(InvoiceStatus.Pending)]
    [InlineData(InvoiceStatus.Confirmed)]
    [InlineData(InvoiceStatus.Returned)]
    public async Task Refuses_while_a_live_invoice_points_at_the_load(InvoiceStatus status)
    {
        var load = Load();
        Invoice(load, status);
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(load.Key, "Erro de montagem", "tester"));

        Assert.Contains("000000123", error.Message);
        Assert.Equal(ShipmentLoadStatus.Open, (await _db.Context.ShipmentLoads.SingleAsync()).Status);
    }

    /// <summary>
    /// Recusa TOTAL para refaturamento: a devolução confirmada zera o consumo e a carga volta a
    /// poder ser cancelada, apesar de continuarem existindo DOIS documentos vivos (a origem
    /// <c>Returned</c> e o retorno <c>Confirmed</c>). Antes desta regra a carga recusada ficava
    /// congelada para sempre, sem saída nenhuma pela tela.
    /// </summary>
    [Fact]
    public async Task Cancels_a_fully_returned_load_even_with_two_live_invoices()
    {
        var load = Load();
        var origin = Invoice(load, InvoiceStatus.Returned);
        Invoice(load, InvoiceStatus.Confirmed, SalesInvoiceType.Return,
            originKey: origin.Key, invoiceNumber: "000000124");
        await _db.Context.SaveChangesAsync();

        await Service().ExecuteAsync(load.Key, "Recusada e desmontada", "tester");

        Assert.Equal(ShipmentLoadStatus.Cancelled, (await _db.Context.ShipmentLoads.SingleAsync()).Status);
    }

    /// <summary>
    /// Devolução ao ARMAZÉM continua travando: o consumo comercial voltou a zero, mas o grão já
    /// está creditado no armazém de destino. Soltar os romaneios os devolveria à Montagem, onde
    /// entrariam em outra carga — o mesmo volume vendido duas vezes E creditado num armazém.
    /// </summary>
    [Fact]
    public async Task Refuses_to_cancel_a_load_whose_goods_went_back_to_a_warehouse()
    {
        var load = Load();

        _db.Context.StorageTransactions.Add(new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = "RM000777",
            CardCode = "C001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            WarehouseCode = "ARM99",
            BranchCode = "01",
            GrossWeight = 30_000m,
            TransactionType = StorageTransactionType.SalesShipmentReturn,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
            RefusedFromShipmentLoadKey = load.Key,
        });
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(load.Key, "Erro de montagem", "tester"));

        Assert.Contains("devolvido(s) ao armazém", error.Message);
        Assert.Equal(ShipmentLoadStatus.Open, (await _db.Context.ShipmentLoads.SingleAsync()).Status);
    }

    [Fact]
    public async Task Cancels_when_every_invoice_is_already_cancelled()
    {
        var load = Load();
        Invoice(load, InvoiceStatus.Cancelled);
        Invoice(load, InvoiceStatus.Cancelled);
        await _db.Context.SaveChangesAsync();

        await Service().ExecuteAsync(load.Key, "Erro de montagem", "tester");

        var saved = await _db.Context.ShipmentLoads.SingleAsync();
        Assert.Equal(ShipmentLoadStatus.Cancelled, saved.Status);
        Assert.Equal("Erro de montagem", saved.CancellationReason);
        Assert.Equal("tester", saved.CanceledBy);
        Assert.NotNull(saved.CanceledAt);
        Assert.Equal(decimal.Zero, saved.InvoicedQuantity);
        Assert.Equal(decimal.Zero, saved.AvailableQuantity);
    }

    [Fact]
    public async Task Cancelling_releases_the_shipments_back_to_assembly()
    {
        var load = Load();
        Shipment(load, "R1");
        Shipment(load, "R2");
        await _db.Context.SaveChangesAsync();

        await Service().ExecuteAsync(load.Key, "Erro de montagem", "tester");

        var shipments = await _db.Context.StorageTransactions.ToListAsync();

        // Voltar para a Montagem é exatamente isto: sem carga e Confirmed de novo — que é o
        // filtro daquela tela.
        Assert.All(shipments, s => Assert.Null(s.ShipmentLoadKey));
        Assert.All(shipments, s => Assert.Equal(StorageTransactionsStatus.Confirmed, s.TransactionStatus));
    }

    [Fact]
    public async Task Cancelling_does_not_touch_a_cancelled_or_returned_shipment()
    {
        var load = Load();
        var cancelled = Shipment(load, "R1");
        cancelled.TransactionStatus = StorageTransactionsStatus.Cancelled;
        var returned = Shipment(load, "R2");
        returned.TransactionStatus = StorageTransactionsStatus.Returned;
        await _db.Context.SaveChangesAsync();

        await Service().ExecuteAsync(load.Key, "Erro de montagem", "tester");

        var saved = await _db.Context.StorageTransactions.ToListAsync();
        Assert.Equal(
            StorageTransactionsStatus.Cancelled,
            saved.Single(x => x.Code == "R1").TransactionStatus);
        Assert.Equal(
            StorageTransactionsStatus.Returned,
            saved.Single(x => x.Code == "R2").TransactionStatus);
    }

    [Fact]
    public async Task Cancelling_records_a_Cancelled_movement()
    {
        var load = Load();
        await _db.Context.SaveChangesAsync();

        await Service().ExecuteAsync(load.Key, "Erro de montagem", "tester");

        var movement = await _db.Context.ShipmentLoadMovements.SingleAsync();
        Assert.Equal(ShipmentLoadMovementType.Cancelled, movement.MovementType);
        Assert.Equal(decimal.Zero, movement.BalanceAfter);
        Assert.Contains("Erro de montagem", movement.Description);
    }

    [Fact]
    public async Task Refuses_without_a_reason()
    {
        var load = Load();
        await _db.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(load.Key, "   ", "tester"));
    }

    [Fact]
    public async Task Refuses_a_load_that_is_already_cancelled()
    {
        var load = Load(ShipmentLoadStatus.Cancelled);
        await _db.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(load.Key, "Erro de montagem", "tester"));
    }
}
