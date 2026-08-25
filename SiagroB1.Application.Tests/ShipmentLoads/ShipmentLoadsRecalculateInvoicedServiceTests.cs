using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// Fórmula do saldo da carga. É persistido-derivado por SOMATÓRIO das notas — não há ledger
/// da carga, porque "esta nota consome X" é inteiramente reconstituível de SALES_INVOICES.
/// </summary>
public class ShipmentLoadsRecalculateInvoicedServiceTests
{
    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();

    private ShipmentLoadsRecalculateInvoicedService Service() => new(_db);

    private ShipmentLoad Load(decimal total = 90_000)
    {
        var load = new ShipmentLoad
        {
            Key = Guid.NewGuid(),
            Code = "CG000007",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            TotalQuantity = total,
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
            GrossWeight = 45_000,
            TransactionType = StorageTransactionType.SalesShipment,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
            ShipmentLoadKey = load.Key,
        };
        _db.Context.StorageTransactions.Add(transaction);
        return transaction;
    }

    private SalesInvoice Invoice(
        ShipmentLoad? load,
        decimal quantity,
        InvoiceStatus status = InvoiceStatus.Confirmed,
        SalesInvoiceType type = SalesInvoiceType.Normal,
        Guid? originKey = null)
    {
        var invoice = new SalesInvoice
        {
            Key = Guid.NewGuid(),
            CardCode = "C001",
            InvoiceNumber = "000000123",
            InvoiceStatus = status,
            InvoiceType = type,
            ShipmentLoadKey = load?.Key,
            SalesInvoiceOriginKey = originKey,
        };

        invoice.Items.Add(new SalesInvoiceItem
        {
            Key = Guid.NewGuid(),
            SalesInvoiceKey = invoice.Key,
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            Quantity = quantity,
        });

        _db.Context.SalesInvoices.Add(invoice);
        return invoice;
    }

    [Fact]
    public async Task No_invoice_leaves_the_load_Open_with_the_full_balance()
    {
        var load = Load();
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(load.Key);

        var saved = await _db.Context.ShipmentLoads.SingleAsync();
        Assert.Equal(decimal.Zero, saved.InvoicedQuantity);
        Assert.Equal(ShipmentLoadStatus.Open, saved.Status);
        Assert.Equal(90_000m, saved.AvailableQuantity);
    }

    [Fact]
    public async Task A_pending_invoice_already_consumes_the_balance()
    {
        // O consumo nasce na CRIAÇÃO da nota, não na confirmação — é o que impede duas notas
        // pendentes reservarem o mesmo volume.
        var load = Load();
        Invoice(load, 40_000, InvoiceStatus.Pending);
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(load.Key);

        var saved = await _db.Context.ShipmentLoads.SingleAsync();
        Assert.Equal(40_000m, saved.InvoicedQuantity);
        Assert.Equal(ShipmentLoadStatus.PartiallyInvoiced, saved.Status);
    }

    [Fact]
    public async Task Two_invoices_covering_the_total_close_the_load_and_the_shipments()
    {
        var load = Load();
        var shipment = Shipment(load, "R1");
        Invoice(load, 40_000);
        Invoice(load, 50_000);
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(load.Key);

        var saved = await _db.Context.ShipmentLoads.SingleAsync();
        Assert.Equal(90_000m, saved.InvoicedQuantity);
        Assert.Equal(ShipmentLoadStatus.Invoiced, saved.Status);
        Assert.Equal(decimal.Zero, saved.AvailableQuantity);

        var savedShipment = await _db.Context.StorageTransactions.SingleAsync(x => x.Key == shipment.Key);
        Assert.Equal(StorageTransactionsStatus.Invoiced, savedShipment.TransactionStatus);
    }

    [Fact]
    public async Task A_cancelled_invoice_does_not_consume()
    {
        var load = Load();
        Invoice(load, 40_000, InvoiceStatus.Cancelled);
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(load.Key);

        Assert.Equal(decimal.Zero, (await _db.Context.ShipmentLoads.SingleAsync()).InvoicedQuantity);
    }

    [Fact]
    public async Task A_returned_origin_keeps_consuming_while_the_return_is_only_pending()
    {
        // SalesInvoicesReturnService marca a origem como Returned já na CRIAÇÃO do retorno,
        // quando nada voltou fisicamente. Se Returned saísse da soma aqui, o saldo reabriria
        // cedo demais e o mesmo volume seria faturado duas vezes.
        var load = Load();
        var origin = Invoice(load, 40_000, InvoiceStatus.Returned);
        Invoice(load, 40_000, InvoiceStatus.Pending, SalesInvoiceType.Return, origin.Key);
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(load.Key);

        Assert.Equal(40_000m, (await _db.Context.ShipmentLoads.SingleAsync()).InvoicedQuantity);
    }

    [Fact]
    public async Task A_confirmed_return_gives_the_balance_back()
    {
        // É o requisito "invoice recusada reabre o saldo da carga": o saldo volta no momento
        // em que a devolução de fato ocorre — a confirmação dela.
        var load = Load();
        var origin = Invoice(load, 40_000, InvoiceStatus.Returned);
        Invoice(load, 40_000, InvoiceStatus.Confirmed, SalesInvoiceType.Return, origin.Key);
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(load.Key);

        var saved = await _db.Context.ShipmentLoads.SingleAsync();
        Assert.Equal(decimal.Zero, saved.InvoicedQuantity);
        Assert.Equal(ShipmentLoadStatus.Open, saved.Status);
    }

    [Fact]
    public async Task A_cancelled_return_consumes_again()
    {
        var load = Load();
        var origin = Invoice(load, 40_000, InvoiceStatus.Returned);
        Invoice(load, 40_000, InvoiceStatus.Cancelled, SalesInvoiceType.Return, origin.Key);
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(load.Key);

        Assert.Equal(40_000m, (await _db.Context.ShipmentLoads.SingleAsync()).InvoicedQuantity);
    }

    [Fact]
    public async Task A_partial_confirmed_return_gives_back_only_its_own_quantity()
    {
        var load = Load();
        var origin = Invoice(load, 40_000, InvoiceStatus.Returned);
        Invoice(load, 15_000, InvoiceStatus.Confirmed, SalesInvoiceType.Return, origin.Key);
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(load.Key);

        Assert.Equal(25_000m, (await _db.Context.ShipmentLoads.SingleAsync()).InvoicedQuantity);
    }

    [Fact]
    public async Task A_standalone_invoice_for_the_same_customer_is_ignored()
    {
        var load = Load();
        Invoice(load: null, quantity: 70_000);
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(load.Key);

        Assert.Equal(decimal.Zero, (await _db.Context.ShipmentLoads.SingleAsync()).InvoicedQuantity);
    }

    [Fact]
    public async Task Excluded_invoices_leave_the_sum_immediately()
    {
        // SumAsync agrega NO SERVIDOR: uma nota removida na mesma transação ainda estaria lá.
        // O parâmetro de exclusão é o que faz o saldo pós-exclusão ficar correto.
        var load = Load();
        var invoice = Invoice(load, 40_000);
        await _db.Context.SaveChangesAsync();

        var invoiced = await ShipmentLoadsRecalculateInvoicedService.CalculateInvoicedAsync(
            _db.Context, load.Key, [invoice.Key]);

        Assert.Equal(decimal.Zero, invoiced);
    }

    [Fact]
    public async Task A_cancelled_load_is_never_touched()
    {
        var load = Load();
        load.Status = ShipmentLoadStatus.Cancelled;
        load.InvoicedQuantity = decimal.Zero;
        Invoice(load, 40_000);
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(load.Key);

        var saved = await _db.Context.ShipmentLoads.SingleAsync();
        Assert.Equal(ShipmentLoadStatus.Cancelled, saved.Status);
        Assert.Equal(decimal.Zero, saved.InvoicedQuantity);
    }

    [Fact]
    public async Task A_cancelled_or_returned_shipment_is_never_reprojected()
    {
        var load = Load();
        var cancelled = Shipment(load, "R1");
        cancelled.TransactionStatus = StorageTransactionsStatus.Cancelled;
        var returned = Shipment(load, "R2");
        returned.TransactionStatus = StorageTransactionsStatus.Returned;
        Invoice(load, 90_000);
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(load.Key);

        var saved = await _db.Context.StorageTransactions.ToListAsync();
        Assert.Equal(StorageTransactionsStatus.Cancelled, saved.Single(x => x.Code == "R1").TransactionStatus);
        Assert.Equal(StorageTransactionsStatus.Returned, saved.Single(x => x.Code == "R2").TransactionStatus);
    }

    [Fact]
    public async Task Recalculating_twice_converges()
    {
        var load = Load();
        Invoice(load, 40_000);
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(load.Key);
        await Service().RecalculateAsync(load.Key);

        var saved = await _db.Context.ShipmentLoads.SingleAsync();
        Assert.Equal(40_000m, saved.InvoicedQuantity);
        Assert.Equal(ShipmentLoadStatus.PartiallyInvoiced, saved.Status);
    }

    [Fact]
    public async Task Reopening_the_balance_puts_the_shipments_back_to_Confirmed()
    {
        var load = Load();
        var shipment = Shipment(load, "R1");
        shipment.TransactionStatus = StorageTransactionsStatus.Invoiced;
        var origin = Invoice(load, 90_000, InvoiceStatus.Returned);
        Invoice(load, 90_000, InvoiceStatus.Confirmed, SalesInvoiceType.Return, origin.Key);
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(load.Key);

        var saved = await _db.Context.StorageTransactions.SingleAsync(x => x.Key == shipment.Key);
        Assert.Equal(StorageTransactionsStatus.Confirmed, saved.TransactionStatus);
    }
}
