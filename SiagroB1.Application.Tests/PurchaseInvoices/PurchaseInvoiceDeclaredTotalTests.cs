using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseInvoices;

/// <summary>
/// Valor declarado do documento de entrada de terceiro Normal na filial que emite NF-e pelo Siagro: como na emissão
/// própria, é gravado pelo sistema — a soma das linhas, recalculada a cada gravação do documento ou de uma linha.
/// Devolução do cliente e filial sem a regra ficam com o valor digitado.
/// </summary>
public class PurchaseInvoiceDeclaredTotalTests
{
    private static PurchaseInvoice ThirdParty(int? usage, decimal typed = 999m)
    {
        var invoice = new PurchaseInvoice
        {
            Key = Guid.NewGuid(), BranchCode = "01", CardCode = PurchaseNfeTestSeed.Supplier,
            IssuerType = DocumentIssuerType.ThirdParty, InvoiceType = PurchaseInvoiceType.Normal,
            TaxDocumentKind = TaxDocumentKind.Other, TotalDocumentValue = typed, IssueDate = new DateTime(2026, 10, 1),
        };
        invoice.AddItem(Line("TRIGO", 1000m, 1.5m, usage));
        return invoice;
    }

    private static PurchaseInvoiceItem Line(string code, decimal quantity, decimal price, int? usage) => new()
    {
        Key = Guid.NewGuid(), ItemCode = code, UnitOfMeasureCode = "KG", Quantity = quantity, UnitPrice = price, UsageCode = usage,
    };

    private static PurchaseInvoicesTaxApplyService Apply(UnitOfWork db, string erp = "STANDALONE") =>
        TaxTestServices.PurchaseApply(db, PurchaseNfeTestSeed.Partners(), erp);

    private static PurchaseInvoicesCreateService Create(UnitOfWork db, string erp = "STANDALONE") =>
        new(db, PurchaseNfeTestSeed.Partners(), new FakeItemService(), Apply(db, erp));

    private static Task<decimal> DeclaredAsync(UnitOfWork db, Guid key) =>
        db.Context.PurchaseInvoices.AsNoTracking().Where(i => i.Key == key).Select(i => i.TotalDocumentValue).SingleAsync();

    private static async Task<(UnitOfWork Db, int Usage)> SeedAsync()
    {
        var db = (await PurchaseNfeTestSeed.SeedAsync()).Db;
        return (db, await PurchaseNfeTestSeed.PurchaseUsageCodeAsync(db));
    }

    [Fact]
    public async Task Create_stores_the_sum_of_the_lines_not_the_typed_value()
    {
        var (db, usage) = await SeedAsync();
        var invoice = ThirdParty(usage);

        await Create(db).ExecuteAsync(invoice, "tester");

        Assert.Equal(1500m, await DeclaredAsync(db, invoice.Key));
    }

    [Fact]
    public async Task Update_recalculates_after_a_quantity_change()
    {
        var (db, usage) = await SeedAsync();
        var invoice = ThirdParty(usage);
        await Create(db).ExecuteAsync(invoice, "tester");
        db.Context.ChangeTracker.Clear();
        var changed = await db.Context.PurchaseInvoices.AsNoTracking().Include(i => i.Items).SingleAsync(i => i.Key == invoice.Key);
        changed.Items.Single().Quantity = 2000m;
        changed.TotalDocumentValue = 1m;

        await new PurchaseInvoicesUpdateService(db, PurchaseNfeTestSeed.Partners(), new FakeItemService(), Apply(db))
            .ExecuteAsync(changed.Key, changed, "tester");

        Assert.Equal(3000m, await DeclaredAsync(db, invoice.Key));
    }

    [Fact]
    public async Task Adding_changing_and_removing_a_line_recalculate()
    {
        var (db, usage) = await SeedAsync();
        var invoice = ThirdParty(usage);
        await Create(db).ExecuteAsync(invoice, "tester");
        var added = Line("MILHO", 10m, 1m, usage);
        added.PurchaseInvoiceKey = invoice.Key;

        await new PurchaseInvoicesItemsCreateService(db, new FakeItemService(), Apply(db)).ExecuteAsync(added, "tester");
        Assert.Equal(1510m, await DeclaredAsync(db, invoice.Key));

        db.Context.ChangeTracker.Clear();
        var patch = await db.Context.PurchaseInvoicesItems.AsNoTracking().SingleAsync(i => i.Key == added.Key);
        patch.Quantity = 20m;
        await new PurchaseInvoicesItemsUpdateService(db, new FakeItemService(), Apply(db)).ExecuteAsync(added.Key!.Value, patch, "tester");
        Assert.Equal(1520m, await DeclaredAsync(db, invoice.Key));

        db.Context.ChangeTracker.Clear();
        await new PurchaseInvoicesItemsDeleteService(db, TaxTestServices.Gate(db, "STANDALONE")).ExecuteAsync(added.Key!.Value);
        Assert.Equal(1500m, await DeclaredAsync(db, invoice.Key));
    }

    [Fact]
    public async Task Customer_return_keeps_the_typed_value()
    {
        var (db, _) = await SeedAsync();
        var invoice = ThirdParty(null);
        invoice.InvoiceType = PurchaseInvoiceType.Return;

        await Create(db).ExecuteAsync(invoice, "tester");

        Assert.Equal(999m, await DeclaredAsync(db, invoice.Key));
    }

    [Fact]
    public async Task Branch_without_the_rule_keeps_the_typed_value()
    {
        var (db, _) = await SeedAsync();
        var invoice = ThirdParty(null);

        await Create(db, "SAPB1").ExecuteAsync(invoice, "tester");

        Assert.Equal(999m, await DeclaredAsync(db, invoice.Key));
    }

    [Fact]
    public async Task Declared_value_is_the_grand_total_of_the_lines()
    {
        var (db, usage) = await SeedAsync();
        var invoice = ThirdParty(usage);
        var line = invoice.Items.Single();
        (line.FreightValue, line.InsuranceValue, line.DiscountValue, line.OtherExpensesValue) = (100m, 20m, 50m, 30m);

        await Create(db).ExecuteAsync(invoice, "tester");

        Assert.Equal(1600m, await DeclaredAsync(db, invoice.Key));
    }

    [Fact]
    public async Task Changing_the_freight_of_a_line_recalculates_the_declared_value()
    {
        var (db, usage) = await SeedAsync();
        var invoice = ThirdParty(usage);
        await Create(db).ExecuteAsync(invoice, "tester");
        db.Context.ChangeTracker.Clear();
        var patch = await db.Context.PurchaseInvoicesItems.AsNoTracking().SingleAsync(i => i.PurchaseInvoiceKey == invoice.Key);
        patch.FreightValue = 250m;

        await new PurchaseInvoicesItemsUpdateService(db, new FakeItemService(), Apply(db)).ExecuteAsync(patch.Key!.Value, patch, "tester");

        Assert.Equal(1750m, await DeclaredAsync(db, invoice.Key));
    }
}
