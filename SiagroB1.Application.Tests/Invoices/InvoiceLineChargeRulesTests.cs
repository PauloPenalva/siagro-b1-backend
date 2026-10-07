using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.SalesInvoices.Factories;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Invoices;

/// <summary>
/// Frete, seguro, desconto e outras despesas na gravação da linha (spec 2026-10-05 §5, R2), nos seis caminhos que gravam
/// linha das duas entidades, e as cópias campo a campo (§4.3). Regra inativa de propósito: a validação vale em toda filial.
/// </summary>
public class InvoiceLineChargeRulesTests
{
    private const string Negative = "frete, seguro, desconto e outras despesas não podem ser negativos.";

    /// <summary>10 × 2,00 = 20,00 de produtos.</summary>
    private static SalesInvoiceItem SalesLine(
        decimal freight = 0m, decimal insurance = 0m, decimal discount = 0m, decimal other = 0m) => new()
    {
        Key = Guid.NewGuid(), ItemCode = "SOJA", UnitOfMeasureCode = "KG", Quantity = 10m, UnitPrice = 2m,
        FreightValue = freight, InsuranceValue = insurance, DiscountValue = discount, OtherExpensesValue = other,
    };

    /// <summary>10 × 2,00 = 20,00 de produtos.</summary>
    private static PurchaseInvoiceItem PurchaseLine(
        decimal freight = 0m, decimal insurance = 0m, decimal discount = 0m, decimal other = 0m) => new()
    {
        Key = Guid.NewGuid(), ItemCode = "TRIGO", UnitOfMeasureCode = "KG", Quantity = 10m, UnitPrice = 2m,
        FreightValue = freight, InsuranceValue = insurance, DiscountValue = discount, OtherExpensesValue = other,
    };

    private static SalesInvoicesCreateService SalesCreate(UnitOfWork db)
    {
        var partners = new FakeBusinessPartnerService();
        var usages = new UsageService(db, NullLogger<UsageService>.Instance);

        return new SalesInvoicesCreateService(
            db, partners, new FakeItemService(), new FakeDocNumberSequenceService(),
            new SalesInvoicesUsageGuardService(usages), new SalesInvoicesCfopResolveService(db, usages, partners),
            TaxTestServices.InactiveApply(db), TaxTestServices.InactiveFiscalComplement(db), NullLogger<SalesInvoicesCreateService>.Instance);
    }

    private static SalesInvoicesItemsUpdateService SalesItemUpdate(UnitOfWork db) =>
        new(db, new FakeItemService(),
            new ShipmentLoadsClosureHookService(db.Context, new ShipmentLoadsChangeLogService(db.Context)),
            TaxTestServices.InactiveApply(db), TaxTestServices.InactiveFiscalComplement(db), NullLogger<SalesInvoicesUpdateService>.Instance);

    private static async Task<(UnitOfWork Db, SalesInvoiceItem Line)> SeedSalesAsync()
    {
        var db = TestDb.CreateUnitOfWork();
        var invoice = new SalesInvoice { Key = Guid.NewGuid(), CardCode = "C1", InvoiceStatus = InvoiceStatus.Pending };
        invoice.AddItem(SalesLine());
        db.Context.SalesInvoices.Add(invoice);
        await db.SaveChangesAsync();
        return (db, invoice.Items.Single());
    }

    private static async Task<(UnitOfWork Db, PurchaseInvoice Invoice)> SeedPurchaseAsync()
    {
        var db = TestDb.CreateUnitOfWork();
        var invoice = new PurchaseInvoice { Key = Guid.NewGuid(), CardCode = "F1" };
        invoice.AddItem(PurchaseLine());
        db.Context.PurchaseInvoices.Add(invoice);
        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
        return (db, invoice);
    }

    // --- a regra ---

    [Theory]
    [InlineData(-0.01, 0, 0, 0)]
    [InlineData(0, -0.01, 0, 0)]
    [InlineData(0, 0, -0.01, 0)]
    [InlineData(0, 0, 0, -0.01)]
    public void Negative_value_is_refused(double freight, double insurance, double discount, double other)
    {
        var line = SalesLine((decimal)freight, (decimal)insurance, (decimal)discount, (decimal)other);

        var e = Assert.Throws<DefaultException>(() => InvoiceLineChargeRules.Ensure(line));

        Assert.Equal($"Item SOJA: {Negative}", e.Message);
    }

    [Fact]
    public void Discount_above_the_line_is_refused()
    {
        // Linha: 20,00 de produtos; o teto do desconto é o valor do produto, mesmo com frete, seguro e despesas.
        var e = Assert.Throws<DefaultException>(() => InvoiceLineChargeRules.Ensure(SalesLine(5m, 1m, 20.01m, 0.5m)));

        Assert.Equal("Item SOJA: o desconto passa do valor do produto da linha.", e.Message);
    }

    [Fact]
    public void Discount_equal_to_the_product_value_is_accepted()
    {
        // Bonificação: o desconto leva o valor do produto inteiro; frete, seguro e despesas continuam a cobrar.
        var line = SalesLine(5m, 1m, 20m, 0.5m);

        InvoiceLineChargeRules.Ensure(line);

        Assert.Equal(6.5m, line.GrandTotal);
    }

    [Fact]
    public void Discount_above_the_product_value_is_refused_even_when_freight_would_cover_it()
    {
        // Caso recusado pela SEFAZ (483): 16,50 de produto, frete 100 e desconto 50 — vDesc não pode passar de vProd.
        var line = new SalesInvoiceItem
        {
            Key = Guid.NewGuid(), ItemCode = "SOJA", UnitOfMeasureCode = "KG", Quantity = 1m, UnitPrice = 16.50m,
            FreightValue = 100m, DiscountValue = 50m,
        };

        var e = Assert.Throws<DefaultException>(() => InvoiceLineChargeRules.Ensure(line));

        Assert.Equal("Item SOJA: o desconto passa do valor do produto da linha.", e.Message);
    }

    [Fact]
    public void Values_are_rounded_to_cents_before_the_check()
    {
        var line = PurchaseLine(freight: 10.005m, insurance: 0.004m);

        InvoiceLineChargeRules.Ensure(line);

        Assert.Equal((10.01m, 0m), (line.FreightValue, line.InsuranceValue));
    }

    // --- documento de saída ---

    [Fact]
    public async Task Sales_document_create_refuses_a_negative_charge()
    {
        var db = TestDb.CreateUnitOfWork();
        var invoice = new SalesInvoice
        {
            Key = Guid.NewGuid(), BranchCode = "01", CardCode = "C1", InvoiceType = SalesInvoiceType.Normal,
            Items = [SalesLine(insurance: -1m)],
        };

        var e = await Assert.ThrowsAsync<DefaultException>(() => SalesCreate(db).ExecuteAsync(invoice, "tester"));

        Assert.Equal($"Item SOJA: {Negative}", e.Message);
    }

    [Fact]
    public async Task Sales_line_create_refuses_a_discount_above_the_line()
    {
        var db = TestDb.CreateUnitOfWork();

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            new SalesInvoicesItemsCreateService(db, new FakeItemService(), TaxTestServices.InactiveApply(db),
                TaxTestServices.InactiveFiscalComplement(db), NullLogger<SalesInvoicesItemsCreateService>.Instance).ExecuteAsync(SalesLine(discount: 20.01m), "tester"));

        Assert.Equal("Item SOJA: o desconto passa do valor do produto da linha.", e.Message);
    }

    [Fact]
    public async Task Sales_line_update_refuses_a_discount_above_the_line()
    {
        var (db, line) = await SeedSalesAsync();
        line.DiscountValue = 20.01m;

        var e = await Assert.ThrowsAsync<DefaultException>(() => SalesItemUpdate(db).ExecuteAsync(line.Key!.Value, line, "tester"));

        Assert.Equal("Item SOJA: o desconto passa do valor do produto da linha.", e.Message);
    }

    [Fact]
    public async Task Sales_line_update_saves_the_charges_in_cents()
    {
        var (db, line) = await SeedSalesAsync();
        (line.FreightValue, line.InsuranceValue, line.DiscountValue, line.OtherExpensesValue) = (12.345m, 1m, 2m, 3m);

        await SalesItemUpdate(db).ExecuteAsync(line.Key!.Value, line, "tester");

        var saved = await db.Context.SalesInvoicesItems.AsNoTracking().SingleAsync();
        Assert.Equal((12.35m, 1m, 2m, 3m), (saved.FreightValue, saved.InsuranceValue, saved.DiscountValue, saved.OtherExpensesValue));
    }

    [Fact]
    public void Document_copy_keeps_the_line_charges()
    {
        var original = new SalesInvoice { Key = Guid.NewGuid(), CardCode = "C1" };
        original.AddItem(SalesLine(5m, 1m, 2m, 0.5m));

        var item = SalesInvoiceCopyFactory.CreateFrom(original, "tester").Items.Single();

        Assert.Equal((5m, 1m, 2m, 0.5m), (item.FreightValue, item.InsuranceValue, item.DiscountValue, item.OtherExpensesValue));
    }

    // --- documento de entrada ---

    [Fact]
    public async Task Purchase_document_create_refuses_a_discount_above_the_line()
    {
        var db = TestDb.CreateUnitOfWork();
        var invoice = new PurchaseInvoice { Key = Guid.NewGuid(), CardCode = "F1" };
        invoice.AddItem(PurchaseLine(discount: 20.01m));

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            new PurchaseInvoicesCreateService(db, new FakeBusinessPartnerService(), new FakeItemService(),
                TaxTestServices.InactivePurchaseApply(db)).ExecuteAsync(invoice, "tester"));

        Assert.Equal("Item TRIGO: o desconto passa do valor do produto da linha.", e.Message);
    }

    [Fact]
    public async Task Purchase_document_update_refuses_a_negative_charge()
    {
        var (db, saved) = await SeedPurchaseAsync();
        var incoming = new PurchaseInvoice { CardCode = "F1" };
        var line = PurchaseLine(other: -1m);
        line.Key = saved.Items.Single().Key;
        incoming.AddItem(line);

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            new PurchaseInvoicesUpdateService(db, new FakeBusinessPartnerService(), new FakeItemService(),
                TaxTestServices.InactivePurchaseApply(db)).ExecuteAsync(saved.Key, incoming, "tester"));

        Assert.Equal($"Item TRIGO: {Negative}", e.Message);
    }

    [Fact]
    public async Task Purchase_document_update_copies_the_charges_of_changed_and_new_lines()
    {
        var (db, saved) = await SeedPurchaseAsync();
        var incoming = new PurchaseInvoice { CardCode = "F1" };
        var changed = PurchaseLine(freight: 10m, insurance: 1m, discount: 2m, other: 3m);
        changed.Key = saved.Items.Single().Key;
        incoming.AddItem(changed);
        var added = PurchaseLine(freight: 4m, insurance: 5m, discount: 6m, other: 7m);
        added.Key = null;
        incoming.AddItem(added);

        await new PurchaseInvoicesUpdateService(db, new FakeBusinessPartnerService(), new FakeItemService(),
            TaxTestServices.InactivePurchaseApply(db)).ExecuteAsync(saved.Key, incoming, "tester");

        var lines = await db.Context.PurchaseInvoicesItems.AsNoTracking().Where(i => i.PurchaseInvoiceKey == saved.Key).ToListAsync();
        var first = lines.Single(i => i.Key == changed.Key);
        var second = lines.Single(i => i.Key != changed.Key);
        Assert.Equal((10m, 1m, 2m, 3m), (first.FreightValue, first.InsuranceValue, first.DiscountValue, first.OtherExpensesValue));
        Assert.Equal((4m, 5m, 6m, 7m), (second.FreightValue, second.InsuranceValue, second.DiscountValue, second.OtherExpensesValue));
    }

    [Fact]
    public async Task Purchase_line_create_refuses_a_negative_charge()
    {
        var db = TestDb.CreateUnitOfWork();

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            new PurchaseInvoicesItemsCreateService(db, new FakeItemService(), TaxTestServices.InactivePurchaseApply(db))
                .ExecuteAsync(PurchaseLine(freight: -1m), "tester"));

        Assert.Equal($"Item TRIGO: {Negative}", e.Message);
    }

    [Fact]
    public async Task Purchase_line_update_refuses_a_discount_above_the_line_and_saves_valid_charges()
    {
        var (db, saved) = await SeedPurchaseAsync();
        var key = saved.Items.Single().Key!.Value;
        var service = new PurchaseInvoicesItemsUpdateService(db, new FakeItemService(), TaxTestServices.InactivePurchaseApply(db));

        var refused = await db.Context.PurchaseInvoicesItems.AsNoTracking().SingleAsync(i => i.Key == key);
        refused.DiscountValue = 20.01m;
        var e = await Assert.ThrowsAsync<DefaultException>(() => service.ExecuteAsync(key, refused, "tester"));
        Assert.Equal("Item TRIGO: o desconto passa do valor do produto da linha.", e.Message);

        db.Context.ChangeTracker.Clear();
        var valid = await db.Context.PurchaseInvoicesItems.AsNoTracking().SingleAsync(i => i.Key == key);
        (valid.FreightValue, valid.InsuranceValue, valid.DiscountValue, valid.OtherExpensesValue) = (3m, 2m, 1m, 0.5m);
        await service.ExecuteAsync(key, valid, "tester");

        var reloaded = await db.Context.PurchaseInvoicesItems.AsNoTracking().SingleAsync(i => i.Key == key);
        Assert.Equal((3m, 2m, 1m, 0.5m), (reloaded.FreightValue, reloaded.InsuranceValue, reloaded.DiscountValue, reloaded.OtherExpensesValue));
    }

    // --- proporção da devolução (D4) ---

    [Theory]
    [InlineData("100.00", "10000", "30000", "33.33")]
    [InlineData("0.05", "15000", "30000", "0.03")]
    [InlineData("50.00", "10000", "30000", "16.67")]
    [InlineData("100.00", "30000", "30000", "100.00")]
    [InlineData("100.00", "0", "0", "0")]
    public void Proportional_value_is_rounded_away_from_zero_in_cents(string value, string returned, string original, string expected)
    {
        decimal D(string s) => decimal.Parse(s, CultureInfo.InvariantCulture);

        Assert.Equal(D(expected), InvoiceLineChargeRules.Proportional(D(value), D(returned), D(original)));
    }

    [Fact]
    public void ApplyProportional_scales_the_four_values_and_clamps_the_discount_at_the_product_value()
    {
        var origin = new SalesInvoiceItem
        {
            ItemCode = "SOJA", UnitOfMeasureCode = "KG", Quantity = 3m, UnitPrice = 150.125m, DiscountValue = 450.38m, FreightValue = 3m, InsuranceValue = 0.03m, OtherExpensesValue = 1m,
        };
        var target = new PurchaseInvoiceItem { ItemCode = "TRIGO", UnitOfMeasureCode = "KG", Quantity = 1m, UnitPrice = 150.125m };

        InvoiceLineChargeRules.ApplyProportional(target, origin);

        Assert.Equal(150.12m, target.Total);
        Assert.Equal((1m, 0.01m, 0.33m), (target.FreightValue, target.InsuranceValue, target.OtherExpensesValue));
        // Teto = valor do produto da devolução (150,12): o desconto proporcional (150,13) é limitado a ele.
        Assert.Equal(150.12m, target.DiscountValue);
    }

    [Fact]
    public void ApplyProportional_caps_the_discount_when_the_proportion_passes_the_ceiling()
    {
        var origin = new SalesInvoiceItem { ItemCode = "SOJA", UnitOfMeasureCode = "KG", Quantity = 3m, UnitPrice = 150.125m, DiscountValue = 450.38m };
        var target = new SalesInvoiceItem { ItemCode = "SOJA", UnitOfMeasureCode = "KG", Quantity = 1m, UnitPrice = 150.125m };

        InvoiceLineChargeRules.ApplyProportional(target, origin);

        Assert.Equal(150.12m, target.DiscountValue);
        InvoiceLineChargeRules.Ensure(target);
    }

    [Fact]
    public void ApplyProportional_clamps_at_the_product_value_even_when_freight_would_allow_more()
    {
        var origin = new SalesInvoiceItem { ItemCode = "SOJA", UnitOfMeasureCode = "KG", Quantity = 1m, UnitPrice = 20m, DiscountValue = 30m, FreightValue = 50m };
        var target = new SalesInvoiceItem { ItemCode = "SOJA", UnitOfMeasureCode = "KG", Quantity = 1m, UnitPrice = 20m };

        InvoiceLineChargeRules.ApplyProportional(target, origin);

        Assert.Equal(50m, target.FreightValue);
        Assert.Equal(20m, target.DiscountValue);
    }
}
