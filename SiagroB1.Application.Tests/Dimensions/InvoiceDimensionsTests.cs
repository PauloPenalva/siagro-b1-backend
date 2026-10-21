using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.Dimensions;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Dimensions;

/// <summary>Dimensões gerenciais e conta contábil nas linhas e no cabeçalho das notas de saída e de entrada.</summary>
public class InvoiceDimensionsTests
{
    private static IConfiguration Config() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Erp"] = "STANDALONE" }).Build();

    private static async Task<UnitOfWork> NewDb()
    {
        var db = TestDb.CreateUnitOfWork();
        await DimensionTestData.SeedDimensions(db);
        db.Context.CostCenters.Add(new CostCenter { Code = "CC01", Name = "Centro 1", DimensionNumber = 1 });
        db.Context.CostCenters.Add(new CostCenter { Code = "CC02", Name = "Centro 2", DimensionNumber = 1 });
        await db.SaveChangesAsync();
        return db;
    }

    private static SalesInvoicesItemsCreateService SalesLineCreate(UnitOfWork db) =>
        new(db, new FakeItemService(), TaxTestServices.InactiveApply(db),
            TaxTestServices.InactiveFiscalComplement(db), NullLogger<SalesInvoicesItemsCreateService>.Instance,
            dimensionValidator: new DimensionCodeValidator(db, Config()),
            dimensionDefaults: new DimensionDefaultsResolver(db, Config()));

    private static PurchaseInvoicesItemsCreateService PurchaseLineCreate(UnitOfWork db) =>
        new(db, new FakeItemService(), TaxTestServices.InactivePurchaseApply(db),
            new DimensionCodeValidator(db, Config()), new DimensionDefaultsResolver(db, Config()));

    private static PurchaseInvoicesItemsUpdateService PurchaseLineUpdate(UnitOfWork db) =>
        new(db, new FakeItemService(), TaxTestServices.InactivePurchaseApply(db),
            new DimensionCodeValidator(db, Config()), new DimensionDefaultsResolver(db, Config()));

    private static SalesInvoicesUpdateService SalesHeaderUpdate(UnitOfWork db) =>
        new(db, new FakeBusinessPartnerService(), TaxTestServices.InactiveApply(db),
            TaxTestServices.InactiveFiscalComplement(db), NullLogger<SalesInvoicesUpdateService>.Instance,
            dimensionValidator: new DimensionCodeValidator(db, Config()),
            dimensionDefaults: new DimensionDefaultsResolver(db, Config()));

    [Fact]
    public async Task New_sales_line_inherits_header_dims()
    {
        var db = await NewDb();
        var invoice = new SalesInvoice
        {
            Key = Guid.NewGuid(), CardCode = "C1", InvoiceStatus = InvoiceStatus.Pending, Dim1Code = "CC01",
        };
        db.Context.SalesInvoices.Add(invoice);
        await db.SaveChangesAsync();

        var line = new SalesInvoiceItem
        {
            Key = Guid.NewGuid(), SalesInvoiceKey = invoice.Key, ItemCode = "SOJA", UnitOfMeasureCode = "KG",
            Quantity = 1m, UnitPrice = 1m,
        };
        await SalesLineCreate(db).ExecuteAsync(line, "tester");

        Assert.Equal("CC01", line.Dim1Code);
    }

    [Fact]
    public async Task New_purchase_line_gets_usage_default()
    {
        var db = await NewDb();
        var usage = new Usage { Name = "Compra", Dim1Code = "CC02" };
        db.Context.Usages.Add(usage);
        var invoice = new PurchaseInvoice { Key = Guid.NewGuid(), CardCode = "F1", InvoiceStatus = InvoiceStatus.Pending };
        db.Context.PurchaseInvoices.Add(invoice);
        await db.SaveChangesAsync();

        var line = new PurchaseInvoiceItem
        {
            PurchaseInvoiceKey = invoice.Key, ItemCode = "SOJA", UnitOfMeasureCode = "KG", Quantity = 1m,
            UsageCode = usage.Code,
        };
        await PurchaseLineCreate(db).ExecuteAsync(line, "tester");

        Assert.Equal("CC02", line.Dim1Code);
    }

    [Fact]
    public async Task Invalid_line_code_rejected_on_update()
    {
        var db = await NewDb();
        var invoice = new PurchaseInvoice { Key = Guid.NewGuid(), CardCode = "F1", InvoiceStatus = InvoiceStatus.Pending };
        var stored = new PurchaseInvoiceItem
        {
            Key = Guid.NewGuid(), PurchaseInvoiceKey = invoice.Key, ItemCode = "SOJA", UnitOfMeasureCode = "KG",
            Quantity = 1m, Dim1Code = "CC01",
        };
        invoice.Items.Add(stored);
        db.Context.PurchaseInvoices.Add(invoice);
        await db.SaveChangesAsync();

        var incoming = new PurchaseInvoiceItem
        {
            Key = stored.Key, PurchaseInvoiceKey = invoice.Key, ItemCode = "SOJA", UnitOfMeasureCode = "KG",
            Quantity = 1m, Dim1Code = "ZZZ",
        };

        var e = await Assert.ThrowsAsync<DefaultException>(
            () => PurchaseLineUpdate(db).ExecuteAsync(stored.Key!.Value, incoming, "tester"));
        Assert.Contains("ZZZ", e.Message);
    }

    [Fact]
    public async Task Changing_header_does_not_touch_existing_lines()
    {
        var db = await NewDb();
        var invoice = new SalesInvoice { Key = Guid.NewGuid(), CardCode = "C1", InvoiceStatus = InvoiceStatus.Pending };
        var line = new SalesInvoiceItem
        {
            Key = Guid.NewGuid(), ItemCode = "SOJA", UnitOfMeasureCode = "KG", Quantity = 1m, UnitPrice = 1m,
        };
        invoice.AddItem(line);
        db.Context.SalesInvoices.Add(invoice);
        await db.SaveChangesAsync();

        // PATCH: a instância rastreada chega já mutada.
        invoice.Dim1Code = " cc01 ";
        await SalesHeaderUpdate(db).ExecuteAsync(invoice.Key, invoice, "tester");

        Assert.Equal("CC01", db.Context.SalesInvoices.Single().Dim1Code);
        Assert.Null(db.Context.SalesInvoicesItems.Single().Dim1Code);
    }
}
