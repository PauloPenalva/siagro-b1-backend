using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.FiscalStock;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using static SiagroB1.Application.Tests.Accounting.Postings.AccountingPostingServiceTests;

namespace SiagroB1.Application.Tests.Accounting.Postings;

/// <summary>CMV no fechamento do mês do estoque fiscal (670) e estorno na reabertura (675).</summary>
public class CostOfSalesPostingTests
{
    private static async Task<IUnitOfWork> SeedAsync(bool withLp = true)
    {
        var db = await AccountingPostingServiceTests.SeedAsync();
        db.Context.LedgerAccounts.Add(new LedgerAccount { Code = "4.2.01", Name = "CMV", Type = LedgerAccountType.Expense });
        db.Context.Items.Add(new Item { ItemCode = "SOJA", ItemName = "SOJA EM GRAOS", LedgerAccountCode = "1.1.03" });
        db.Context.FiscalInventoryOpeningBalances.Add(new FiscalInventoryOpeningBalance
        {
            BranchCode = "01", ItemCode = "SOJA", ItemName = "SOJA EM GRAOS", UnitOfMeasureCode = "SC",
            BalanceDate = new DateTime(2026, 9, 1), Quantity = 100m, TotalValue = 5000m,
        });
        if (withLp)
            db.Context.StandardPostings.Add(Lp("670",
                Seq("001", "Fixed", "4.2.01", "Item.LedgerAccount", null, "Item.CostOfSales", "CMV {ItemCode}"),
                Seq("002", "Item.LedgerAccount", null, "Fixed", "4.2.01", "Item.SalesReturnCost", "Devolução {ItemCode}")));
        await db.SaveChangesAsync();
        return db;
    }

    private static SalesInvoiceItem Line(decimal quantity, string cfop, Guid? origin = null)
    {
        var line = InvoiceReportSeed.SaleItem("SOJA", "SOJA EM GRAOS", quantity, 80m, "SC");
        line.Cfop = cfop;
        line.MovesFiscalInventory = true;
        line.SalesInvoiceItemOriginKey = origin;
        return line;
    }

    private static async Task<SalesInvoiceItem> SellAsync(IUnitOfWork db, decimal quantity, string cfop = "5102",
        int day = 10)
    {
        var line = Line(quantity, cfop);
        var sale = InvoiceReportSeed.Sale("DS" + day, new DateTime(2026, 9, day), items: line);
        sale.TaxDocumentKind = TaxDocumentKind.Other;
        db.Context.SalesInvoices.Add(sale);
        await db.SaveChangesAsync();
        return line;
    }

    private static FiscalStockPeriodsService Periods(IUnitOfWork db) => new(db, accounting: Service(db));

    private static Task<List<JournalEntry>> Entries(IUnitOfWork db) =>
        db.Context.JournalEntries.AsNoTracking().Include(e => e.Lines).OrderBy(e => e.DocEntry).ToListAsync();

    [Fact]
    public async Task Close_posts_670_per_item_with_fifo_cost()
    {
        var db = await SeedAsync();
        await SellAsync(db, 40m);

        var period = await Periods(db).CloseAsync("01", 2026, 9, "admin");

        var entry = (await Entries(db)).Single();
        Assert.Equal("670", entry.StandardPostingCode);
        Assert.Equal(new DateTime(2026, 9, 30), entry.PostingDate);
        Assert.Equal(period.Key.ToString(), entry.SourceDocumentKey);
        Assert.Equal(2000m, entry.Lines.Single().Amount); // 40 × 50
        Assert.Equal(("4.2.01", "1.1.03"), (entry.Lines.Single().DebitAccountCode, entry.Lines.Single().CreditAccountCode));
        Assert.Equal("008870", entry.Batch);
    }

    [Fact]
    public async Task Sales_return_posts_its_own_line()
    {
        var db = await SeedAsync();
        var sold = await SellAsync(db, 40m);
        var back = Line(10m, "1202", sold.Key);
        var ret = InvoiceReportSeed.Sale("DV1", new DateTime(2026, 9, 20), type: SalesInvoiceType.Return, items: back);
        ret.TaxDocumentKind = TaxDocumentKind.Other;
        db.Context.SalesInvoices.Add(ret);
        await db.SaveChangesAsync();

        await Periods(db).CloseAsync("01", 2026, 9, "admin");

        // Custo das saídas e custo devolvido saem em linhas próprias: nada se perde num saldo líquido negativo.
        var lines = (await Entries(db)).Single().Lines.OrderBy(l => l.LineNumber).ToList();
        Assert.Equal([2000m, 500m], lines.Select(l => l.Amount));
        Assert.Equal("4.2.01", lines[1].CreditAccountCode);
    }

    [Fact]
    public async Task Reopen_reverses_670()
    {
        var db = await SeedAsync();
        await SellAsync(db, 40m);
        await Periods(db).CloseAsync("01", 2026, 9, "admin");

        await Periods(db).ReopenAsync("01", 2026, 9, "ajuste", "admin");

        var entries = await Entries(db);
        Assert.Equal(2, entries.Count);
        Assert.Equal(entries[0].DocEntry, entries[1].ReversalOfDocEntry);
    }

    [Fact]
    public async Task Reclose_after_reopen_posts_again()
    {
        var db = await SeedAsync();
        await SellAsync(db, 40m);
        await Periods(db).CloseAsync("01", 2026, 9, "admin");
        await Periods(db).ReopenAsync("01", 2026, 9, "ajuste", "admin");
        await Periods(db).CloseAsync("01", 2026, 9, "admin");

        Assert.Equal(3, (await Entries(db)).Count);
    }

    [Fact]
    public async Task Close_without_active_670_does_not_block()
    {
        var db = await SeedAsync(withLp: false);
        await SellAsync(db, 40m);
        var period = await Periods(db).CloseAsync("01", 2026, 9, "admin");
        Assert.Equal(FiscalStockPeriodStatus.Closed, period.Status);
        Assert.Empty(await Entries(db));
    }

    [Fact]
    public async Task Remittance_exit_is_not_cost_of_sales()
    {
        var db = await SeedAsync();
        await SellAsync(db, 40m);
        await SellAsync(db, 20m, cfop: "5905", day: 12); // remessa para depósito: sai do estoque, mas não é venda

        await Periods(db).CloseAsync("01", 2026, 9, "admin");

        Assert.Equal(2000m, (await Entries(db)).Single().Lines.Single().Amount);
    }

    [Fact]
    public async Task Close_in_closed_accounting_month_posts_on_first_open_day()
    {
        var db = await SeedAsync();
        await SellAsync(db, 40m);
        db.Context.AccountingPeriods.Add(new AccountingPeriod
        {
            Year = 2026, Month = 9, StartDate = new DateTime(2026, 9, 1), EndDate = new DateTime(2026, 9, 30),
            Status = AccountingPeriodStatus.Closed,
        });
        await db.SaveChangesAsync();

        var period = await Periods(db).CloseAsync("01", 2026, 9, "admin");

        Assert.Equal(FiscalStockPeriodStatus.Closed, period.Status);
        Assert.Equal(new DateTime(2026, 10, 1), (await Entries(db)).Single().PostingDate);
    }
}
