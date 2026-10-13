using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;
using SiagroB1.Infra.Queries;
using static SiagroB1.Application.Tests.PurchaseFiscalReconciliations.FiscalReconciliationSeed;

namespace SiagroB1.Application.Tests.PurchaseFiscalReconciliations;

public class PurchaseFiscalReconciliationQueryTests
{
    private static FiscalReconciliationFilter Filter(Action<FiscalReconciliationFilter>? change = null)
    {
        var f = new FiscalReconciliationFilter { CardCode = Supplier, ItemCode = Product };
        change?.Invoke(f);
        return f;
    }

    [Fact]
    public async Task Transactions_bring_physical_fiscal_status_contracts_and_links()
    {
        var seed = await FiscalReconciliationSeed.CreateAsync();
        var contract = await seed.AddContractAsync("PC-7");
        var tx = await seed.AddTransactionAsync(29_820m, contract: contract, code: "RM000001");
        var (_, line) = await seed.AddInvoiceAsync(30_000m, taxNumber: "000123");
        await seed.LinkAsync(line, tx, 30_000m);

        var row = Assert.Single(await PurchaseFiscalReconciliationQuery.LoadTransactionsAsync(seed.Db.Context, Filter()));

        Assert.Equal("RM000001", row.Code);
        Assert.Equal(29_820m, row.PhysicalQuantity);
        Assert.Equal(30_000m, row.FiscalQuantity);
        Assert.Equal(180m, row.Difference);
        Assert.Equal(FiscalReconciliationStatus.Partial, row.Status);
        Assert.Equal("Parcial", row.StatusName);
        Assert.Equal(["PC-7"], row.ContractCodes);
        Assert.Equal("000123-1", row.InvoiceNumbersText);
    }

    [Fact]
    public async Task Transactions_skip_cancelled_non_purchase_other_supplier_and_ignore_cancelled_invoice_links()
    {
        var seed = await FiscalReconciliationSeed.CreateAsync();
        var tx = await seed.AddTransactionAsync(100m);
        await seed.AddTransactionAsync(100m, status: StorageTransactionsStatus.Cancelled);
        await seed.AddTransactionAsync(100m, type: StorageTransactionType.Receipt);
        await seed.AddTransactionAsync(100m, cardCode: "F9999");
        var (_, line) = await seed.AddInvoiceAsync(100m, status: InvoiceStatus.Cancelled);
        await seed.LinkAsync(line, tx, 100m);

        var row = Assert.Single(await PurchaseFiscalReconciliationQuery.LoadTransactionsAsync(seed.Db.Context, Filter()));

        Assert.Equal(FiscalReconciliationStatus.WithoutInvoice, row.Status);
        Assert.Equal(0m, row.FiscalQuantity);
    }

    [Fact]
    public async Task Transactions_filter_by_contract_code_and_inclusive_period()
    {
        var seed = await FiscalReconciliationSeed.CreateAsync();
        var c1 = await seed.AddContractAsync("PC-1");
        var c2 = await seed.AddContractAsync("PC-2");
        await seed.AddTransactionAsync(10m, contract: c1, date: new DateTime(2026, 10, 31, 23, 0, 0), code: "RM-A");
        await seed.AddTransactionAsync(10m, contract: c2, date: new DateTime(2026, 10, 15), code: "RM-B");
        await seed.AddTransactionAsync(10m, contract: c1, date: new DateTime(2026, 11, 1), code: "RM-C");

        var rows = await PurchaseFiscalReconciliationQuery.LoadTransactionsAsync(seed.Db.Context, Filter(f =>
        {
            f.ContractCode = "PC-1";
            f.DateFrom = new DateTime(2026, 10, 1);
            f.DateTo = new DateTime(2026, 10, 31);
        }));

        Assert.Equal(["RM-A"], rows.Select(r => r.Code));
    }

    [Fact]
    public async Task Confirmed_transaction_is_reconciled_even_with_difference()
    {
        var seed = await FiscalReconciliationSeed.CreateAsync();
        var tx = await seed.AddTransactionAsync(100m);
        tx.FiscalReconciledAt = DateTime.Now;
        var (_, line) = await seed.AddInvoiceAsync(90m);
        await seed.LinkAsync(line, tx, 90m);

        var row = Assert.Single(await PurchaseFiscalReconciliationQuery.LoadTransactionsAsync(seed.Db.Context, Filter()));

        Assert.Equal(FiscalReconciliationStatus.Reconciled, row.Status);
    }

    [Fact]
    public async Task Invoice_lines_default_to_supplier_and_same_owner_issuers()
    {
        var seed = await FiscalReconciliationSeed.CreateAsync();
        await seed.AddPartnerAsync("F0002", "Produtor Fazenda B (outra IE)", "123.456.789-01");
        await seed.AddPartnerAsync("F0003", "Outro produtor", "98765432100");
        await seed.AddInvoiceAsync(100m, cardCode: Supplier, taxNumber: "1");
        await seed.AddInvoiceAsync(200m, cardCode: "F0002", taxNumber: "2");
        await seed.AddInvoiceAsync(300m, cardCode: "F0003", taxNumber: "3");

        var lines = await PurchaseFiscalReconciliationQuery.LoadInvoiceLinesAsync(seed.Db.Context, Filter());

        Assert.Equal(["1-1", "2-1"], lines.Select(l => l.TaxDocument).Order());
        Assert.All(lines, l => Assert.True(l.IssuerMatchesSupplier));
    }

    [Fact]
    public async Task Invoice_lines_with_explicit_other_issuer_carry_the_warning_flag()
    {
        var seed = await FiscalReconciliationSeed.CreateAsync();
        await seed.AddPartnerAsync("F0003", "Outro produtor", "98765432100");
        await seed.AddInvoiceAsync(300m, cardCode: "F0003", taxNumber: "3");

        var line = Assert.Single(await PurchaseFiscalReconciliationQuery.LoadInvoiceLinesAsync(
            seed.Db.Context, Filter(f => f.IssuerCardCode = "F0003")));

        Assert.False(line.IssuerMatchesSupplier);
    }

    [Fact]
    public async Task Invoice_lines_convert_to_kg_hide_fully_linked_and_skip_cancelled_and_counter_notes()
    {
        var seed = await FiscalReconciliationSeed.CreateAsync();
        seed.Db.Context.ItemComplements.Add(new ItemComplement { ItemCode = Product, CommercialUnitOfMeasureCode = "SC", CommercialFactor = 60m });
        var tx = await seed.AddTransactionAsync(6_000m);
        await seed.AddInvoiceAsync(100m, unit: "SC", taxNumber: "1");
        var (_, full) = await seed.AddInvoiceAsync(500m, taxNumber: "2");
        await seed.LinkAsync(full, tx, 500m);
        await seed.AddInvoiceAsync(10m, unit: "TON", taxNumber: "3");
        await seed.AddInvoiceAsync(10m, status: InvoiceStatus.Cancelled, taxNumber: "4");
        await seed.AddInvoiceAsync(10m, producerInvoiceKey: Guid.NewGuid(), taxNumber: "5");

        var lines = await PurchaseFiscalReconciliationQuery.LoadInvoiceLinesAsync(seed.Db.Context, Filter());

        Assert.Equal(["1-1", "3-1"], lines.Select(l => l.TaxDocument).Order());
        Assert.Equal(6_000m, lines.Single(l => l.TaxDocument == "1-1").AvailableQuantity);
        Assert.Null(lines.Single(l => l.TaxDocument == "3-1").QuantityKg);

        var all = await PurchaseFiscalReconciliationQuery.LoadInvoiceLinesAsync(seed.Db.Context, Filter(f => f.IncludeFullyLinked = true));
        Assert.Equal(0m, all.Single(l => l.TaxDocument == "2-1").AvailableQuantity);
    }

    [Fact]
    public async Task Invoice_lines_skip_custody_notes()
    {
        var seed = await FiscalReconciliationSeed.CreateAsync();
        await seed.AddInvoiceAsync(100m, taxNumber: "1");
        await seed.AddInvoiceAsync(500m, taxNumber: "2", custodyTransactionKey: Guid.NewGuid());

        var lines = await PurchaseFiscalReconciliationQuery.LoadInvoiceLinesAsync(seed.Db.Context, Filter());

        Assert.Equal(["1-1"], lines.Select(l => l.TaxDocument));
    }

    [Fact]
    public async Task Masked_cnpj_partners_are_the_same_owner()
    {
        var seed = await FiscalReconciliationSeed.CreateAsync();
        await seed.AddPartnerAsync("PJ01", "Cerealista Matriz", "12.345.678/0001-90");
        await seed.AddPartnerAsync("PJ02", "Cerealista Filial", "12.345.678/0002-71");
        await seed.AddInvoiceAsync(100m, cardCode: "PJ02", taxNumber: "9");

        var line = Assert.Single(await PurchaseFiscalReconciliationQuery.LoadInvoiceLinesAsync(
            seed.Db.Context, Filter(f => f.CardCode = "PJ01")));

        Assert.True(line.IssuerMatchesSupplier);
    }

    [Fact]
    public void Queries_translate_to_sql()
    {
        using var ctx = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=model-inspection-only;Database=none").Options);
        var filter = Filter(f => { f.ContractCode = "PC-1"; f.DateFrom = DateTime.Today; f.DateTo = DateTime.Today; });

        Assert.NotEmpty(PurchaseFiscalReconciliationQuery.TransactionsBase(ctx, filter).ToQueryString());
        Assert.NotEmpty(PurchaseFiscalReconciliationQuery.LinksQuery(ctx, [Guid.NewGuid()]).ToQueryString());
        Assert.NotEmpty(PurchaseFiscalReconciliationQuery.ContractsQuery(ctx, [Guid.NewGuid()]).ToQueryString());
        Assert.NotEmpty(PurchaseFiscalReconciliationQuery.InvoiceLinesBase(ctx, filter, ["F0001"]).ToQueryString());
    }
}
