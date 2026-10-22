using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.Accounting;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using static SiagroB1.Application.Tests.Accounting.AccountingTestData;

namespace SiagroB1.Application.Tests.Accounting;

/// <summary>Centro de custo, item contábil, rateio e conta nas linhas e no cabeçalho das notas de saída e de entrada.</summary>
public class InvoiceAccountingAssignmentTests
{
    private static async Task<UnitOfWork> NewDb()
    {
        var db = TestDb.CreateUnitOfWork();
        await SeedAsync(db);
        return db;
    }

    private static SalesInvoicesItemsCreateService SalesLineCreate(UnitOfWork db) =>
        new(db, new FakeItemService(), TaxTestServices.InactiveApply(db),
            TaxTestServices.InactiveFiscalComplement(db), NullLogger<SalesInvoicesItemsCreateService>.Instance,
            dimensionValidator: new AccountingAssignmentValidator(db, Config()),
            dimensionDefaults: new AccountingAssignmentDefaultsResolver(db, Config()));

    private static PurchaseInvoicesItemsCreateService PurchaseLineCreate(UnitOfWork db) =>
        new(db, new FakeItemService(), TaxTestServices.InactivePurchaseApply(db),
            new AccountingAssignmentValidator(db, Config()), new AccountingAssignmentDefaultsResolver(db, Config()));

    private static PurchaseInvoicesItemsUpdateService PurchaseLineUpdate(UnitOfWork db) =>
        new(db, new FakeItemService(), TaxTestServices.InactivePurchaseApply(db),
            new AccountingAssignmentValidator(db, Config()), new AccountingAssignmentDefaultsResolver(db, Config()));

    private static SalesInvoicesUpdateService SalesHeaderUpdate(UnitOfWork db) =>
        new(db, new FakeBusinessPartnerService(), TaxTestServices.InactiveApply(db),
            TaxTestServices.InactiveFiscalComplement(db), NullLogger<SalesInvoicesUpdateService>.Instance,
            dimensionValidator: new AccountingAssignmentValidator(db, Config()),
            dimensionDefaults: new AccountingAssignmentDefaultsResolver(db, Config()));

    [Fact]
    public async Task New_sales_line_inherits_header_assignment()
    {
        var db = await NewDb();
        var invoice = new SalesInvoice
        {
            Key = Guid.NewGuid(), CardCode = "C1", InvoiceStatus = InvoiceStatus.Pending, CostCenterCode = "CC01",
            AccountingItemCode = "IT01",
        };
        db.Context.SalesInvoices.Add(invoice);
        await db.SaveChangesAsync();

        var line = new SalesInvoiceItem
        {
            Key = Guid.NewGuid(), SalesInvoiceKey = invoice.Key, ItemCode = "SOJA", UnitOfMeasureCode = "KG",
            Quantity = 1m, UnitPrice = 1m,
        };
        await SalesLineCreate(db).ExecuteAsync(line, "tester");

        Assert.Equal("CC01", line.CostCenterCode);
        Assert.Equal("IT01", line.AccountingItemCode);
    }

    [Fact]
    public async Task New_purchase_line_gets_header_rule()
    {
        var db = await NewDb();
        await AddRuleAsync(db, Rule("R01", Today.AddYears(-1), ("CC01", null, 60), ("CC02", null, 40)));
        var invoice = new PurchaseInvoice { Key = Guid.NewGuid(), CardCode = "F1", InvoiceStatus = InvoiceStatus.Pending, DistributionRuleCode = "R01" };
        db.Context.PurchaseInvoices.Add(invoice);
        await db.SaveChangesAsync();

        var line = new PurchaseInvoiceItem
        {
            PurchaseInvoiceKey = invoice.Key, ItemCode = "SOJA", UnitOfMeasureCode = "KG", Quantity = 1m,
        };
        await PurchaseLineCreate(db).ExecuteAsync(line, "tester");

        Assert.Equal("R01", line.DistributionRuleCode);
        Assert.Null(line.CostCenterCode);
    }

    [Fact]
    public async Task Purchase_line_validity_uses_the_document_date()
    {
        // FUT vale a partir de amanhã: num documento datado depois, é aceito; num datado hoje, recusado.
        var db = await NewDb();
        var later = new PurchaseInvoice
        {
            Key = Guid.NewGuid(), CardCode = "F1", InvoiceStatus = InvoiceStatus.Pending,
            IssueDate = Today.AddDays(5), PostingDate = Today.AddDays(5),
        };
        var today = new PurchaseInvoice
        {
            Key = Guid.NewGuid(), CardCode = "F1", InvoiceStatus = InvoiceStatus.Pending,
            IssueDate = Today, PostingDate = Today,
        };
        db.Context.PurchaseInvoices.AddRange(later, today);
        await db.SaveChangesAsync();

        var ok = new PurchaseInvoiceItem
        {
            PurchaseInvoiceKey = later.Key, ItemCode = "SOJA", UnitOfMeasureCode = "KG", Quantity = 1m,
            CostCenterCode = "FUT",
        };
        await PurchaseLineCreate(db).ExecuteAsync(ok, "tester");
        Assert.Equal("FUT", ok.CostCenterCode);

        var bad = new PurchaseInvoiceItem
        {
            PurchaseInvoiceKey = today.Key, ItemCode = "SOJA", UnitOfMeasureCode = "KG", Quantity = 1m,
            CostCenterCode = "FUT",
        };
        await Assert.ThrowsAsync<DefaultException>(() => PurchaseLineCreate(db).ExecuteAsync(bad, "tester"));
    }

    [Fact]
    public async Task Invalid_line_code_rejected_on_update()
    {
        var db = await NewDb();
        var invoice = new PurchaseInvoice { Key = Guid.NewGuid(), CardCode = "F1", InvoiceStatus = InvoiceStatus.Pending };
        var stored = new PurchaseInvoiceItem
        {
            Key = Guid.NewGuid(), PurchaseInvoiceKey = invoice.Key, ItemCode = "SOJA", UnitOfMeasureCode = "KG",
            Quantity = 1m, CostCenterCode = "CC01",
        };
        invoice.Items.Add(stored);
        db.Context.PurchaseInvoices.Add(invoice);
        await db.SaveChangesAsync();

        var incoming = new PurchaseInvoiceItem
        {
            Key = stored.Key, PurchaseInvoiceKey = invoice.Key, ItemCode = "SOJA", UnitOfMeasureCode = "KG",
            Quantity = 1m, CostCenterCode = "SYN",
        };

        var e = await Assert.ThrowsAsync<DefaultException>(
            () => PurchaseLineUpdate(db).ExecuteAsync(stored.Key!.Value, incoming, "tester"));
        Assert.Contains("SYN", e.Message);
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
        invoice.CostCenterCode = " cc01 ";
        await SalesHeaderUpdate(db).ExecuteAsync(invoice.Key, invoice, "tester");

        Assert.Equal("CC01", db.Context.SalesInvoices.Single().CostCenterCode);
        Assert.Null(db.Context.SalesInvoicesItems.Single().CostCenterCode);
    }
}
