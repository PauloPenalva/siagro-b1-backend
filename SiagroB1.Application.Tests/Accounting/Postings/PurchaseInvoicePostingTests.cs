using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Accounting.Postings;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using static SiagroB1.Application.Tests.Accounting.Postings.AccountingPostingServiceTests;

namespace SiagroB1.Application.Tests.Accounting.Postings;

/// <summary>Documento de Entrada: 650/660 na confirmação; 655/665 no estorno da confirmação e no cancelamento.</summary>
public class PurchaseInvoicePostingTests
{
    private static readonly DateTime Posting = new(2026, 10, 10);

    private static string _name = string.Empty;

    private static async Task<(UnitOfWork Db, PurchaseInvoice Invoice)> SeedAsync(bool moves = true, bool withLp660 = true)
    {
        var name = Guid.NewGuid().ToString();
        _name = name;
        await AccountingPostingServiceTests.SeedAsync(name);
        var db = TestDb.CreateUnitOfWork(name);
        if (withLp660)
            db.Context.StandardPostings.Add(Lp("660", Seq("001", "Fixed", "4.1.01", "Partner.LedgerAccount", "2.1.01", "Document.TotalFunrural")));
        db.Context.Items.Add(new Item { ItemCode = "10001", ItemName = "SOJA", LedgerAccountCode = "1.1.03" });

        var item = InvoiceReportSeed.PurchaseItem();
        item.IcmsValue = 120m; // Total = 10 × 100 = 1.000
        InvoiceReportSeed.SetFunrural(item, 1000m, 1.2m, 0.1m, 0.2m); // 12 + 1 + 2 = 15
        item.MovesFiscalInventory = moves;
        var invoice = InvoiceReportSeed.Purchase("NF1", Posting, InvoiceStatus.Pending, items: item);
        db.Context.PurchaseInvoices.Add(invoice);
        await db.SaveChangesAsync();
        return (db, invoice);
    }

    private static IAccountingPostingService Engine(IUnitOfWork db) => Service(db);

    private static async Task<List<JournalEntry>> Entries(IUnitOfWork db) =>
        await db.Context.JournalEntries.AsNoTracking().Include(e => e.Lines).OrderBy(e => e.DocEntry).ToListAsync();

    private static Task Confirm(UnitOfWork db, PurchaseInvoice invoice) =>
        TaxTestServices.PurchaseConfirm(db, accounting: Engine(db)).ExecuteAsync(invoice.Key, "admin");

    [Fact]
    public async Task Confirm_posts_650_per_line_and_660_total()
    {
        var (db, invoice) = await SeedAsync();
        await Confirm(db, invoice);

        var entries = await Entries(db);
        var e650 = entries.Single(e => e.StandardPostingCode == "650");
        Assert.Equal(880m, e650.Lines.Single().Amount); // GrandTotal − ICMS, o valor da camada FIFO
        Assert.Equal("1.1.03", e650.Lines.Single().DebitAccountCode);
        Assert.Equal(Posting, e650.PostingDate);
        Assert.Equal(invoice.Key.ToString(), e650.SourceDocumentKey);
        Assert.Equal("NF NF1 10001", e650.Lines.Single().History);
        Assert.Equal(15m, entries.Single(e => e.StandardPostingCode == "660").Lines.Single().Amount);
    }

    [Fact]
    public async Task FiscalStockCost_is_grandtotal_minus_icms_only_when_moves_inventory()
    {
        var (db, invoice) = await SeedAsync(moves: false, withLp660: false);
        await Confirm(db, invoice);
        Assert.Empty(await Entries(db)); // FiscalStockCost = 0 → sem linha → sem lançamento
    }

    [Fact]
    public async Task Confirm_without_active_lp_does_not_block()
    {
        var (db, invoice) = await SeedAsync(withLp660: false);
        (await db.Context.StandardPostings.SingleAsync(p => p.Code == "650")).Active = false;
        await db.SaveChangesAsync();

        await Confirm(db, invoice);

        Assert.Equal(InvoiceStatus.Confirmed, (await db.Context.PurchaseInvoices.AsNoTracking().SingleAsync()).InvoiceStatus);
        Assert.Empty(await Entries(db));
    }

    [Fact]
    public async Task Confirm_with_broken_lp_fails_confirmation()
    {
        var (db, invoice) = await SeedAsync(withLp660: false);
        var name = _name;
        (await db.Context.Items.SingleAsync()).LedgerAccountCode = null;
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Confirm(db, invoice));
        Assert.StartsWith("LP 650 seq. 001: conta de débito não encontrada", ex.Message);
        Assert.Equal(InvoiceStatus.Pending, (await TestDb.CreateUnitOfWork(name).Context.PurchaseInvoices.SingleAsync()).InvoiceStatus);
    }

    [Fact]
    public async Task Reverse_confirm_posts_655_and_reconfirm_posts_again()
    {
        var (db, invoice) = await SeedAsync(withLp660: false);
        await Confirm(db, invoice);
        await FinancialDocumentTestServices.PurchaseReverse(db, Engine(db)).ExecuteAsync(invoice.Key, "admin");
        await Confirm(db, invoice);

        var entries = await Entries(db);
        Assert.Equal(["650", "655", "650"], entries.Select(e => e.StandardPostingCode));
        Assert.Equal(entries[0].DocEntry, entries[1].ReversalOfDocEntry);
        Assert.Null(entries[2].ReversedByDocEntry);
    }

    [Fact]
    public async Task Cancel_of_confirmed_invoice_posts_655()
    {
        var (db, invoice) = await SeedAsync(withLp660: false);
        await Confirm(db, invoice);
        await FinancialDocumentTestServices.PurchaseCancel(db, Engine(db)).ExecuteAsync(invoice.Key, "admin");
        Assert.Equal(["650", "655"], (await Entries(db)).Select(e => e.StandardPostingCode));
    }

    [Fact]
    public async Task Cancel_of_pending_invoice_posts_nothing()
    {
        var (db, invoice) = await SeedAsync(withLp660: false);
        await FinancialDocumentTestServices.PurchaseCancel(db, Engine(db)).ExecuteAsync(invoice.Key, "admin");
        Assert.Empty(await Entries(db));
    }
}
