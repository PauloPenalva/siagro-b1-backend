using SiagroB1.Application.Services.Accounting.Offline;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using static SiagroB1.Application.Tests.Accounting.Postings.AccountingPostingServiceTests;

namespace SiagroB1.Application.Tests.Accounting.Offline;

/// <summary>Coletores da contabilização off-line: o que ainda falta contabilizar, por módulo.</summary>
public class OfflinePostingSourcesTests
{
    private static readonly DateTime From = new(2026, 1, 1);
    private static readonly DateTime To = new(2026, 12, 31);

    public static async Task<IUnitOfWork> SeedAsync()
    {
        var db = await Postings.AccountingPostingServiceTests.SeedAsync(); // corte 01/01/2026, LP 650/655 ativos
        db.Context.StandardPostings.AddRange(
            Lp("660", Seq("001", "Fixed", "4.1.01", "Fixed", "2.1.01", "Document.TotalFunrural")),
            Lp("610", Seq("001", "Fixed", "2.1.01", "Fixed", "4.1.01", "Line.Total")),
            Lp("620", Seq("001", "Fixed", "2.1.01", "Fixed", "4.1.01", "Document.Total")),
            Lp("510", Seq("001", "Fixed", "4.1.01", "Fixed", "2.1.01", "Document.NetAmount")),
            Lp("530", Seq("001", "Fixed", "2.1.01", "Fixed", "1.1.03", "Settlement.Amount")),
            Lp("562", Seq("001", "Fixed", "4.1.01", "Fixed", "1.1.03", "Entry.Amount")),
            Lp("560", Seq("001", "Fixed", "1.1.03", "Fixed", "1.1.04", "Transfer.Amount")));
        db.Context.Branchs.AddRange(new Branch { Code = "01", BranchName = "Matriz" }, new Branch { Code = "02", BranchName = "Filial" });
        await db.SaveChangesAsync();
        return db;
    }

    public static JournalEntry Posted(string code, string type, string key, string evt) => new()
    {
        PostingDate = new DateTime(2026, 7, 16), BranchCode = "01", Batch = "008810", Origin = JournalEntryOrigin.StandardPosting,
        StandardPostingCode = code, SourceDocumentType = type, SourceDocumentKey = key, SourceEvent = evt, CreatedBy = "t",
    };

    private static async Task<List<OfflinePostingEvent>> Collect(IOfflinePostingSource source, string? branch = null)
    {
        var list = new List<OfflinePostingEvent>();
        await foreach (var e in source.CollectAsync(From, To, branch, CancellationToken.None)) list.Add(e);
        return list;
    }

    private static async Task<PurchaseInvoice> AddPurchase(IUnitOfWork db, InvoiceStatus status = InvoiceStatus.Confirmed)
    {
        var invoice = InvoiceReportSeed.Purchase("NF1", status: status);
        db.Context.PurchaseInvoices.Add(invoice);
        await db.SaveChangesAsync();
        return invoice;
    }

    [Fact]
    public async Task Purchase_source_lists_confirmed_invoice_without_entry()
    {
        var db = await SeedAsync();
        var invoice = await AddPurchase(db);

        var e = Assert.Single(await Collect(new PurchaseInvoiceOfflineSource(db)));
        Assert.Equal(invoice.Key.ToString(), e.DocumentKey);
        Assert.Equal(["650", "660"], e.Codes);
        Assert.Equal(new DateTime(2026, 7, 16), e.Date); // data de estoque = PostingDate
        Assert.Equal("NF1", e.DocumentNumber);
    }

    [Fact]
    public async Task Purchase_source_skips_invoice_already_posted()
    {
        var db = await SeedAsync();
        var invoice = await AddPurchase(db);
        db.Context.JournalEntries.AddRange(Posted("650", "PurchaseInvoice", invoice.Key.ToString(), "Confirm"),
            Posted("660", "PurchaseInvoice", invoice.Key.ToString(), "Confirm"));
        await db.SaveChangesAsync();

        Assert.Empty(await Collect(new PurchaseInvoiceOfflineSource(db)));
    }

    [Fact] // [RF 4]
    public async Task Purchase_source_returns_only_missing_code()
    {
        var db = await SeedAsync();
        var invoice = await AddPurchase(db);
        db.Context.JournalEntries.Add(Posted("650", "PurchaseInvoice", invoice.Key.ToString(), "Confirm"));
        await db.SaveChangesAsync();

        Assert.Equal(["660"], Assert.Single(await Collect(new PurchaseInvoiceOfflineSource(db))).Codes);
    }

    [Fact]
    public async Task Purchase_source_reports_cancelled_invoice_as_no_net_effect()
    {
        var db = await SeedAsync();
        await AddPurchase(db, InvoiceStatus.Cancelled);

        var e = Assert.Single(await Collect(new PurchaseInvoiceOfflineSource(db)));
        Assert.Empty(e.Codes);
        Assert.Equal(OfflinePostingLookup.NoNetEffect, e.Note);
    }

    [Fact]
    public async Task Pending_invoice_is_not_collected()
    {
        var db = await SeedAsync();
        await AddPurchase(db, InvoiceStatus.Pending);
        Assert.Empty(await Collect(new PurchaseInvoiceOfflineSource(db)));
    }

    [Fact]
    public async Task Sales_source_includes_returned_status()
    {
        var db = await SeedAsync();
        db.Context.SalesInvoices.AddRange(InvoiceReportSeed.Sale("S1"), InvoiceReportSeed.Sale("S2", status: InvoiceStatus.Returned),
            InvoiceReportSeed.Sale("S3", status: InvoiceStatus.Pending));
        await db.SaveChangesAsync();

        var events = await Collect(new SalesInvoiceOfflineSource(db));
        Assert.Equal(["S1", "S2"], events.Select(e => e.DocumentNumber).Order().ToList());
        Assert.All(events, e => Assert.Equal(["610", "620"], e.Codes));
    }

    [Fact]
    public async Task Source_ignores_inactive_lp_code()
    {
        var db = await SeedAsync();
        foreach (var lp in db.Context.StandardPostings.Where(p => p.Code == "660")) lp.Active = false;
        await AddPurchase(db);

        Assert.Equal(["650"], Assert.Single(await Collect(new PurchaseInvoiceOfflineSource(db))).Codes);
    }

    [Fact]
    public async Task Event_without_date_uses_approved_at_with_note()
    {
        var db = await SeedAsync();
        var invoice = InvoiceReportSeed.Purchase("NF9");
        invoice.PostingDate = null;
        invoice.IssueDate = null;
        invoice.ApprovedAt = new DateTime(2026, 8, 3, 10, 0, 0);
        db.Context.PurchaseInvoices.Add(invoice);
        await db.SaveChangesAsync();

        var e = Assert.Single(await Collect(new PurchaseInvoiceOfflineSource(db)));
        Assert.Equal(new DateTime(2026, 8, 3), e.Date);
        Assert.Equal(OfflinePostingLookup.DocumentDateUsed, e.Note);
    }

    private static async Task SeedFinancialAsync(IUnitOfWork db, string? titleBranch = "01")
    {
        db.Context.FinancialAccounts.Add(new FinancialAccount
        {
            Code = "BCO1", Name = "Banco", Type = FinancialAccountType.Bank, Currency = CurrencyType.Brl, LedgerAccountCode = "1.1.03",
        });
        db.Context.FinancialAccounts.Add(new FinancialAccount
        {
            Code = "BCO2", Name = "Banco 2", Type = FinancialAccountType.Bank, Currency = CurrencyType.Brl, LedgerAccountCode = "1.1.04",
        });
        var manual = Title("FN-1", FinancialDocumentOrigin.Manual, titleBranch);
        var fromInvoice = Title("FN-2", FinancialDocumentOrigin.PurchaseInvoice, "01");
        var provisional = Title("FN-3", FinancialDocumentOrigin.Manual, "01");
        provisional.Nature = FinancialDocumentNature.Provisional;
        var canceled = Title("FN-4", FinancialDocumentOrigin.Manual, "01");
        canceled.Status = FinancialDocumentStatus.Canceled;
        db.Context.FinancialDocuments.AddRange(manual, fromInvoice, provisional, canceled);

        var paid = Settlement(manual, 100m);
        var undone = Settlement(manual, 50m);
        var undo = Settlement(manual, -50m);
        undo.ReversedSettlementKey = undone.Key;
        db.Context.FinancialSettlements.AddRange(paid, undone, undo, Settlement(provisional, 10m));

        var transfer = new FinancialAccountTransfer
        {
            Key = Guid.NewGuid(), FromAccountCode = "BCO1", ToAccountCode = "BCO2", TransferDate = new DateTime(2026, 7, 20),
            Amount = 30m, Description = "Transf",
        };
        db.Context.FinancialAccountTransfers.Add(transfer);
        db.Context.FinancialAccountEntries.AddRange(
            new FinancialAccountEntry { Key = Guid.NewGuid(), FinancialAccountCode = "BCO1", EntryDate = new DateTime(2026, 7, 21), Amount = -20m, Description = "Tarifa" },
            new FinancialAccountEntry { Key = Guid.NewGuid(), FinancialAccountCode = "BCO1", EntryDate = new DateTime(2026, 7, 20), Amount = -30m, Description = "Perna", TransferKey = transfer.Key },
            new FinancialAccountEntry { Key = Guid.NewGuid(), FinancialAccountCode = "BCO2", EntryDate = new DateTime(2026, 7, 20), Amount = 30m, Description = "Perna", TransferKey = transfer.Key });
        await db.SaveChangesAsync();
    }

    private static FinancialDocument Title(string code, FinancialDocumentOrigin origin, string? branch) => new()
    {
        Key = Guid.NewGuid(), Code = code, CardCode = "F1", CardName = "FORNECEDOR", BranchCode = branch,
        Direction = FinancialDirection.Payable, Nature = FinancialDocumentNature.Firm, Status = FinancialDocumentStatus.Open,
        DocumentDate = new DateTime(2026, 7, 10), DueDate = new DateTime(2026, 8, 10), NetAmount = 500m, Currency = CurrencyType.Brl,
        OriginType = origin,
    };

    private static FinancialSettlement Settlement(FinancialDocument title, decimal amount) => new()
    {
        Key = Guid.NewGuid(), FinancialDocumentKey = title.Key, FinancialAccountCode = "BCO1",
        SettlementDate = new DateTime(2026, 7, 15), Amount = amount, Origin = FinancialSettlementOrigin.Manual,
    };

    [Fact]
    public async Task Financial_source_skips_titles_from_invoices_and_provisional()
    {
        var db = await SeedAsync();
        await SeedFinancialAsync(db);

        var titles = (await Collect(new FinancialOfflineSource(db))).Where(e => e.DocumentType == "FinancialDocument").ToList();
        Assert.Equal(["FN-1"], titles.Select(e => e.DocumentNumber));
        Assert.Equal(["510"], titles[0].Codes);
    }

    [Fact]
    public async Task Financial_source_skips_reversed_settlement()
    {
        var db = await SeedAsync();
        await SeedFinancialAsync(db);

        var settlement = Assert.Single((await Collect(new FinancialOfflineSource(db))).Where(e => e.DocumentType == "FinancialSettlement"));
        Assert.Equal(["530"], settlement.Codes);
        Assert.Equal("FN-1", settlement.DocumentNumber);
    }

    [Fact]
    public async Task Financial_source_skips_transfer_legs()
    {
        var db = await SeedAsync();
        await SeedFinancialAsync(db);

        var events = await Collect(new FinancialOfflineSource(db));
        Assert.Equal(["562"], Assert.Single(events.Where(e => e.DocumentType == "FinancialAccountEntry")).Codes);
        Assert.Equal(["560"], Assert.Single(events.Where(e => e.DocumentType == "FinancialAccountTransfer")).Codes);
    }

    [Fact] // [RF 5]
    public async Task Branch_filter_uses_resolved_branch()
    {
        var db = await SeedAsync();
        await SeedFinancialAsync(db, titleBranch: null); // título sem filial → menor filial ("01")

        Assert.Contains(await Collect(new FinancialOfflineSource(db), "01"), e => e.DocumentNumber == "FN-1" && e.DocumentType == "FinancialDocument");
        Assert.DoesNotContain(await Collect(new FinancialOfflineSource(db), "02"), e => e.DocumentNumber == "FN-1");
    }
}
