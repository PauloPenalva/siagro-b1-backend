using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Invoices;

/// <summary>Pedido do cliente (xPed/nItemPed) na linha do documento de saída: a regra vale na gravação direta da linha.</summary>
public class CustomerOrderRulesTests
{
    private const string ItemMessage = "O item do pedido do cliente tem de 1 a 6 dígitos.";
    private const string NumberMessage = "O pedido do cliente tem no máximo 15 caracteres.";

    private static SalesInvoiceItem Line(string? number = null, string? item = null) => new()
    {
        Key = Guid.NewGuid(), ItemCode = "SOJA", UnitOfMeasureCode = "KG", Quantity = 10m, UnitPrice = 2m,
        CustomerOrderNumber = number, CustomerOrderItem = item,
    };

    private static SalesInvoicesItemsCreateService Create(UnitOfWork db) =>
        new(db, new FakeItemService(), TaxTestServices.InactiveApply(db),
            TaxTestServices.InactiveFiscalComplement(db), NullLogger<SalesInvoicesItemsCreateService>.Instance);

    private static SalesInvoicesItemsUpdateService Update(UnitOfWork db) =>
        new(db, new FakeItemService(),
            new ShipmentLoadsClosureHookService(db.Context, new ShipmentLoadsChangeLogService(db.Context)),
            TaxTestServices.InactiveApply(db), TaxTestServices.InactiveFiscalComplement(db), NullLogger<SalesInvoicesUpdateService>.Instance);

    private static async Task<(UnitOfWork Db, SalesInvoiceItem Line)> SeedAsync()
    {
        var db = TestDb.CreateUnitOfWork();
        var invoice = new SalesInvoice { Key = Guid.NewGuid(), CardCode = "C1", InvoiceStatus = InvoiceStatus.Pending };
        invoice.AddItem(Line());
        db.Context.SalesInvoices.Add(invoice);
        await db.SaveChangesAsync();
        return (db, invoice.Items.Single());
    }

    [Theory]
    [InlineData("A1")]
    [InlineData("0")]
    [InlineData("000")]
    [InlineData("1234567")]
    public void Invalid_order_item_is_refused(string value)
    {
        var e = Assert.Throws<DefaultException>(() => CustomerOrderRules.Ensure(Line(item: value)));

        Assert.Equal(ItemMessage, e.Message);
    }

    [Fact]
    public void Order_number_above_fifteen_is_refused()
    {
        var e = Assert.Throws<DefaultException>(() => CustomerOrderRules.Ensure(Line(number: new string('P', 16))));

        Assert.Equal(NumberMessage, e.Message);
    }

    [Fact]
    public void Blank_values_become_null_and_valid_ones_are_trimmed()
    {
        var blank = Line(" ", "  ");
        CustomerOrderRules.Ensure(blank);
        Assert.Equal((null, null), (blank.CustomerOrderNumber, blank.CustomerOrderItem));

        var valid = Line(" PO-77 ", " 10 ");
        CustomerOrderRules.Ensure(valid);
        Assert.Equal(("PO-77", "10"), (valid.CustomerOrderNumber, valid.CustomerOrderItem));
    }

    [Theory]
    [InlineData("A1")]
    [InlineData("0")]
    public async Task Line_create_refuses_an_invalid_order_item(string value)
    {
        var db = TestDb.CreateUnitOfWork();

        var e = await Assert.ThrowsAsync<DefaultException>(() => Create(db).ExecuteAsync(Line(item: value), "tester"));

        Assert.Equal(ItemMessage, e.Message);
    }

    [Fact]
    public async Task Line_create_accepts_a_valid_order()
    {
        var db = TestDb.CreateUnitOfWork();

        await Create(db).ExecuteAsync(Line("PO-77", "10"), "tester");

        var saved = await db.Context.SalesInvoicesItems.AsNoTracking().SingleAsync();
        Assert.Equal(("PO-77", "10"), (saved.CustomerOrderNumber, saved.CustomerOrderItem));
    }

    [Theory]
    [InlineData("A1")]
    [InlineData("0")]
    public async Task Line_update_refuses_an_invalid_order_item(string value)
    {
        var (db, line) = await SeedAsync();
        line.CustomerOrderItem = value;

        var e = await Assert.ThrowsAsync<DefaultException>(() => Update(db).ExecuteAsync(line.Key!.Value, line, "tester"));

        Assert.Equal(ItemMessage, e.Message);
    }

    [Fact]
    public async Task Line_update_accepts_a_valid_order()
    {
        var (db, line) = await SeedAsync();
        (line.CustomerOrderNumber, line.CustomerOrderItem) = ("PO-77", "10");

        await Update(db).ExecuteAsync(line.Key!.Value, line, "tester");

        var saved = await db.Context.SalesInvoicesItems.AsNoTracking().SingleAsync();
        Assert.Equal(("PO-77", "10"), (saved.CustomerOrderNumber, saved.CustomerOrderItem));
    }
}
