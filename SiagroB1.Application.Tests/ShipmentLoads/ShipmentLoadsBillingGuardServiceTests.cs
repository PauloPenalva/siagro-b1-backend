using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// Invariante I2 — "Σ das notas vivas ≤ TotalQuantity". Não há CHECK nem índice que force
/// isso (é soma sobre outra tabela), então este guard é a única camada lógica; a proteção
/// real contra duas notas simultâneas é o RowVersion da carga.
/// </summary>
public class ShipmentLoadsBillingGuardServiceTests
{
    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();

    private ShipmentLoadsBillingGuardService Guard() => new(_db.Context);

    private ShipmentLoad Load(
        decimal total = 90_000,
        decimal persistedInvoiced = 0,
        ShipmentLoadStatus status = ShipmentLoadStatus.Open)
    {
        var load = new ShipmentLoad
        {
            Key = Guid.NewGuid(),
            Code = "CG000007",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            TotalQuantity = total,
            InvoicedQuantity = persistedInvoiced,
            Status = status,
        };
        _db.Context.ShipmentLoads.Add(load);
        return load;
    }

    private void Invoice(ShipmentLoad load, decimal quantity, InvoiceStatus status = InvoiceStatus.Confirmed)
    {
        var invoice = new SalesInvoice
        {
            Key = Guid.NewGuid(),
            CardCode = "C001",
            InvoiceStatus = status,
            InvoiceType = SalesInvoiceType.Normal,
            ShipmentLoadKey = load.Key,
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
    }

    [Fact]
    public async Task Allows_billing_exactly_the_remaining_balance()
    {
        var load = Load();
        Invoice(load, 40_000);
        await _db.Context.SaveChangesAsync();

        await Guard().EnsureCanBillAsync(load.Key, 50_000);
    }

    [Fact]
    public async Task Refuses_more_than_the_remaining_balance()
    {
        var load = Load();
        Invoice(load, 40_000);
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Guard().EnsureCanBillAsync(load.Key, 50_001));

        Assert.Contains("CG000007", error.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Refuses_a_non_positive_quantity(decimal quantity)
    {
        var load = Load();
        await _db.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<ApplicationException>(
            () => Guard().EnsureCanBillAsync(load.Key, quantity));
    }

    [Fact]
    public async Task Refuses_a_cancelled_load()
    {
        var load = Load(status: ShipmentLoadStatus.Cancelled);
        await _db.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<ApplicationException>(
            () => Guard().EnsureCanBillAsync(load.Key, 1_000));
    }

    [Fact]
    public async Task Refuses_a_load_that_does_not_exist()
    {
        await Assert.ThrowsAsync<ApplicationException>(
            () => Guard().EnsureCanBillAsync(Guid.NewGuid(), 1_000));
    }

    [Fact]
    public async Task Decides_by_the_recalculated_balance_not_the_persisted_one()
    {
        // Drift proposital: o persistido diz que está tudo faturado, o ledger de notas diz
        // que nada foi. Ler o persistido barraria um faturamento legítimo — mesmo precedente
        // de SalesShipmentReleasesCloseService.
        var load = Load(persistedInvoiced: 90_000);
        await _db.Context.SaveChangesAsync();

        await Guard().EnsureCanBillAsync(load.Key, 90_000);
    }

    [Fact]
    public async Task A_cancelled_invoice_frees_the_balance_again()
    {
        var load = Load();
        Invoice(load, 90_000, InvoiceStatus.Cancelled);
        await _db.Context.SaveChangesAsync();

        await Guard().EnsureCanBillAsync(load.Key, 90_000);
    }

    [Fact]
    public async Task Tolerates_a_thousandth_over_the_balance()
    {
        // A tolerância existe porque quantidade é DECIMAL(18,3): sem ela, um arredondamento
        // no cliente recusaria o faturamento do último milésimo de uma carga.
        var load = Load();
        Invoice(load, 40_000);
        await _db.Context.SaveChangesAsync();

        await Guard().EnsureCanBillAsync(load.Key, 50_000.001m);
    }

    /// <summary>
    /// Carga apenas planejada é recusada PELO STATUS, e não pela comparação de saldo.
    /// </summary>
    /// <remarks>
    /// Sem esta cláusula ela seria recusada de qualquer jeito — volume zero, saldo zero —, mas
    /// com uma mensagem sobre quantidade, que manda o usuário procurar um problema que não
    /// existe. O que falta é vincular romaneio, e a mensagem precisa dizer isso.
    /// </remarks>
    [Fact]
    public async Task A_planned_load_is_refused_with_an_actionable_message()
    {
        var load = Load(total: 0, status: ShipmentLoadStatus.Planned);
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Guard().EnsureCanBillAsync(load.Key, 10_000m));

        Assert.Contains("CG000007", error.Message);
        Assert.Contains("planejada", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("romaneio", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
