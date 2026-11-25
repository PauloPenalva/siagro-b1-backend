using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// Sexto termo do saldo da carga (GAC-1226): a divergência entre o efetivo e a NF do SAP vinculada.
/// Só conta enquanto o registro está aberto e o documento está vivo e não totalmente devolvido.
/// </summary>
public class ShipmentLoadsRecalculateDivergenceServiceTests
{
    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();

    private ShipmentLoad Load(decimal total = 30_000)
    {
        var load = new ShipmentLoad
        {
            Key = Guid.NewGuid(),
            Code = "CG000026",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            TotalQuantity = total,
        };
        _db.Context.ShipmentLoads.Add(load);
        return load;
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

    private SalesInvoiceDivergence Divergence(
        ShipmentLoad load,
        SalesInvoice invoice,
        decimal difference,
        SalesInvoiceDivergenceStatus status = SalesInvoiceDivergenceStatus.Open)
    {
        var divergence = new SalesInvoiceDivergence
        {
            Key = Guid.NewGuid(),
            SalesInvoiceKey = invoice.Key,
            ShipmentLoadKey = load.Key,
            LinkType = SapLinkType.QuantityAndValueDivergent,
            EffectiveQuantity = load.TotalQuantity,
            InvoiceQuantity = load.TotalQuantity - difference,
            QuantityDifference = difference,
            InvoiceValue = 1_000m,
            Status = status,
            CreatedAt = DateTime.Now,
        };
        _db.Context.SalesInvoiceDivergences.Add(divergence);
        return divergence;
    }

    private Task<decimal> Calculate(ShipmentLoad load, ICollection<Guid>? excluded = null) =>
        ShipmentLoadsRecalculateDivergenceService.CalculateAsync(_db.Context, load.Key, excluded);

    [Fact]
    public async Task An_open_divergence_of_a_confirmed_invoice_counts()
    {
        var load = Load();
        var invoice = Invoice(load, 29_800);
        Divergence(load, invoice, 200);
        await _db.Context.SaveChangesAsync();

        Assert.Equal(200m, await Calculate(load));
    }

    [Fact]
    public async Task A_voided_divergence_does_not_count()
    {
        var load = Load();
        var invoice = Invoice(load, 29_800);
        Divergence(load, invoice, 200, SalesInvoiceDivergenceStatus.Voided);
        await _db.Context.SaveChangesAsync();

        Assert.Equal(decimal.Zero, await Calculate(load));
    }

    [Fact]
    public async Task A_cancelled_invoice_with_an_open_divergence_does_not_count()
    {
        var load = Load();
        var invoice = Invoice(load, 29_800, InvoiceStatus.Cancelled);
        Divergence(load, invoice, 200);
        await _db.Context.SaveChangesAsync();

        Assert.Equal(decimal.Zero, await Calculate(load));
    }

    [Fact]
    public async Task A_fully_returned_invoice_does_not_count()
    {
        var load = Load();
        var invoice = Invoice(load, 29_800, InvoiceStatus.Returned);
        Divergence(load, invoice, 200);
        Invoice(null, 29_800, InvoiceStatus.Confirmed, SalesInvoiceType.Return, invoice.Key);
        await _db.Context.SaveChangesAsync();

        Assert.Equal(decimal.Zero, await Calculate(load));
    }

    [Fact]
    public async Task A_partially_returned_invoice_keeps_counting()
    {
        var load = Load();
        var invoice = Invoice(load, 29_800, InvoiceStatus.Returned);
        Divergence(load, invoice, 200);
        Invoice(null, 10_000, InvoiceStatus.Confirmed, SalesInvoiceType.Return, invoice.Key);
        await _db.Context.SaveChangesAsync();

        Assert.Equal(200m, await Calculate(load));
    }

    [Fact]
    public async Task Cancelling_the_return_counts_again_without_touching_the_record()
    {
        var load = Load();
        var invoice = Invoice(load, 29_800, InvoiceStatus.Returned);
        var divergence = Divergence(load, invoice, 200);
        var ret = Invoice(null, 29_800, InvoiceStatus.Confirmed, SalesInvoiceType.Return, invoice.Key);
        await _db.Context.SaveChangesAsync();
        Assert.Equal(decimal.Zero, await Calculate(load));

        ret.InvoiceStatus = InvoiceStatus.Cancelled;
        await _db.Context.SaveChangesAsync();

        Assert.Equal(200m, await Calculate(load));
        Assert.Equal(SalesInvoiceDivergenceStatus.Open, divergence.Status);
    }

    [Fact]
    public async Task An_excluded_invoice_leaves_the_sum_immediately()
    {
        var load = Load();
        var invoice = Invoice(load, 29_800);
        Divergence(load, invoice, 200);
        await _db.Context.SaveChangesAsync();

        Assert.Equal(decimal.Zero, await Calculate(load, [invoice.Key]));
    }

    [Fact]
    public async Task Billing_guard_available_subtracts_the_divergence()
    {
        var load = Load();
        var invoice = Invoice(load, 29_800);
        Divergence(load, invoice, 200);
        await _db.Context.SaveChangesAsync();

        var available = await new ShipmentLoadsBillingGuardService(_db.Context).CalculateAvailableAsync(load.Key);

        Assert.Equal(decimal.Zero, available);
    }
}
