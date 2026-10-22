using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Accounting;
using SiagroB1.Application.Services.Accounting.Postings;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Accounting;
using static SiagroB1.Application.Tests.Accounting.AccountingTestData;

namespace SiagroB1.Application.Tests.Accounting.Postings;

/// <summary>Motor da contabilização automática (DetProva/cA100Incl do Protheus) sobre um contexto neutro.</summary>
public class AccountingPostingServiceTests
{
    public static readonly DateTime Day = new(2026, 10, 21);

    /// <summary>Plano mínimo + corte em 01/01/2026 + LP 650 (D estoque pelo produto / C fornecedor fixo) e 655.</summary>
    public static async Task<IUnitOfWork> SeedAsync(string? name = null, bool cutover = true)
    {
        var db = TestDb.CreateUnitOfWork(name ?? Guid.NewGuid().ToString());
        await AccountingTestData.SeedAsync(db);
        db.Context.LedgerAccounts.AddRange(
            new LedgerAccount { Code = "1.1.03", Name = "Estoque", Type = LedgerAccountType.Asset },
            new LedgerAccount { Code = "1.1.04", Name = "Estoque B", Type = LedgerAccountType.Asset },
            new LedgerAccount { Code = "2.1.01", Name = "Fornecedores", Type = LedgerAccountType.Liability },
            new LedgerAccount { Code = "4.1.01", Name = "Despesa", Type = LedgerAccountType.Expense });
        if (cutover) db.Context.AccountingSettings.Add(new AccountingSettings { CutoverDate = new DateTime(2026, 1, 1), UpdatedBy = "t" });
        db.Context.StandardPostings.Add(Lp("650", Seq("001", "Item.LedgerAccount", null, "Fixed", "2.1.01", "Line.FiscalStockCost", "NF {DocNumber} {ItemCode}")));
        db.Context.StandardPostings.Add(Lp("655", Seq("001", "Fixed", "2.1.01", "Item.LedgerAccount", null, "Line.FiscalStockCost", "Estorno NF {DocNumber}")));
        await db.SaveChangesAsync();
        return db;
    }

    public static StandardPosting Lp(string code, params StandardPostingLine[] lines) =>
        new() { Code = code, Active = true, UpdatedBy = "t", Lines = lines.ToList() };

    public static StandardPostingLine Seq(string seq, string? debitSource, string? debitCode, string? creditSource,
        string? creditCode, string value, string history = "H") => new()
    {
        Sequence = seq, EntryType = JournalEntryLineType.Double, DebitAccountSource = debitSource, DebitAccountCode = debitCode,
        CreditAccountSource = creditSource, CreditAccountCode = creditCode, ValueSource = value, HistoryTemplate = history,
        Active = true,
    };

    public static PostingRow Row(string item, decimal cost, string? itemAccount = "1.1.03", string? lineAccount = null,
        string? partnerAccount = null, string? cc = null, string? rule = null) => new(
        new Dictionary<string, string?>
        {
            ["Line.LedgerAccount"] = lineAccount, ["Item.LedgerAccount"] = itemAccount, ["Partner.LedgerAccount"] = partnerAccount,
        },
        new Dictionary<string, decimal> { ["Line.FiscalStockCost"] = cost, ["Line.Total"] = cost },
        cc, null, rule,
        new Dictionary<string, string> { ["{DocNumber}"] = "123", ["{ItemCode}"] = item });

    public static PostingContext Ctx(string evt = "Confirm", DateTime? date = null, bool external = false, params PostingRow[] rows) =>
        new("PurchaseInvoice", "K1", evt, date ?? Day, "01", "F1", rows.Length > 0 ? rows : [Row("SOJA", 100m)], external);

    public static AccountingPostingService Service(IUnitOfWork db, string erp = "STANDALONE")
    {
        var guard = new AccountingPeriodGuard(db);
        var posting = new JournalEntryPostingService(db, guard);
        return new AccountingPostingService(db, Config(erp), posting, new JournalEntriesService(db, posting), guard,
            new AccountingSettingsService(db), new DistributionRuleExpander(db));
    }

    private static List<JournalEntry> Added(IUnitOfWork db) =>
        db.Context.ChangeTracker.Entries<JournalEntry>().Where(e => e.State == EntityState.Added).Select(e => e.Entity).ToList();

    [Fact]
    public async Task NoOp_in_sapb1() =>
        Assert.Null(await Service(await SeedAsync(), "SAPB1").PostAsync("650", Ctx(), "u"));

    [Fact]
    public async Task NoOp_without_cutover() =>
        Assert.Null(await Service(await SeedAsync(cutover: false)).PostAsync("650", Ctx(), "u"));

    [Fact]
    public async Task NoOp_before_cutover() =>
        Assert.Null(await Service(await SeedAsync()).PostAsync("650", Ctx(date: new DateTime(2025, 12, 31)), "u"));

    [Fact]
    public async Task NoOp_when_lp_inactive()
    {
        var db = await SeedAsync();
        (await db.Context.StandardPostings.SingleAsync(p => p.Code == "650")).Active = false;
        await db.SaveChangesAsync();
        Assert.Null(await Service(db).PostAsync("650", Ctx(), "u"));
        Assert.Null(await Service(db).PostAsync("660", Ctx(), "u"));
    }

    [Fact]
    public async Task NoOp_when_already_posted_for_same_event()
    {
        var db = await SeedAsync();
        Assert.NotNull(await Service(db).PostAsync("650", Ctx(), "u"));
        Assert.Null(await Service(db).PostAsync("650", Ctx(), "u")); // ainda só no ChangeTracker
        await db.SaveChangesAsync();
        Assert.Null(await Service(db).PostAsync("650", Ctx(), "u")); // agora no banco
    }

    [Fact]
    public async Task Per_line_lp_generates_one_line_per_row_and_sequence()
    {
        var db = await SeedAsync();
        var lp = await db.Context.StandardPostings.Include(p => p.Lines).SingleAsync(p => p.Code == "650");
        lp.Lines.Add(Seq("002", "Fixed", "4.1.01", "Fixed", "2.1.01", "Line.Total"));
        await db.SaveChangesAsync();

        var entry = await Service(db).PostAsync("650", Ctx(rows: [Row("SOJA", 100m), Row("MILHO", 40m)]), "u");

        Assert.Equal(4, entry!.Lines.Count(l => l.EntryType == JournalEntryLineType.Double));
        Assert.Equal(JournalEntryOrigin.StandardPosting, entry.Origin);
        Assert.Equal("650", entry.StandardPostingCode);
        Assert.Equal(("PurchaseInvoice", "K1", "Confirm"), (entry.SourceDocumentType, entry.SourceDocumentKey, entry.SourceEvent));
        Assert.Equal("008810", entry.Batch);
        Assert.Equal("F1", entry.Lines[0].BusinessPartnerCode);
    }

    [Fact]
    public async Task Zero_value_skips_line_and_no_lines_is_noop()
    {
        var db = await SeedAsync();
        var entry = await Service(db).PostAsync("650", Ctx(rows: [Row("SOJA", 100m), Row("FRETE", 0m)]), "u");
        Assert.Single(entry!.Lines);
        Assert.Null(await Service(await SeedAsync()).PostAsync("650", Ctx(rows: [Row("FRETE", 0m)]), "u"));
    }

    [Fact]
    public async Task Account_fallback_line_item_partner_fixed()
    {
        var db = await SeedAsync();
        var lp = await db.Context.StandardPostings.Include(p => p.Lines).SingleAsync(p => p.Code == "650");
        lp.Lines[0].DebitAccountSource = "Line.LedgerAccount";
        lp.Lines[0].DebitAccountCode = "4.1.01";
        await db.SaveChangesAsync();

        var entry = await Service(db).PostAsync("650", Ctx(rows:
        [
            Row("A", 1m, itemAccount: null, lineAccount: "1.1.04"),
            Row("B", 1m, itemAccount: "1.1.03"),
            Row("C", 1m, itemAccount: null, partnerAccount: "1.1.04"),
            Row("D", 1m, itemAccount: null),
        ]), "u");

        Assert.Equal(["1.1.04", "1.1.03", "1.1.04", "4.1.01"], entry!.Lines.Select(l => l.DebitAccountCode));
    }

    [Fact]
    public async Task Missing_account_throws_with_lp_prefix()
    {
        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            Service(SeedAsync().Result).PostAsync("650", Ctx(rows: [Row("SOJA", 10m, itemAccount: null)]), "u"));
        Assert.Equal("LP 650 seq. 001: conta de débito não encontrada para SOJA.", ex.Message);
    }

    [Fact]
    public async Task Distribution_rule_splits_with_rounding_on_last()
    {
        var db = await SeedAsync();
        await AddRuleAsync(db, Rule("R3", new DateTime(2026, 1, 1), ("CC01", null, 33.33m), ("CC02", null, 33.33m), ("CC01", "IT01", 33.34m)));
        var lp = await db.Context.StandardPostings.Include(p => p.Lines).SingleAsync(p => p.Code == "650");
        lp.Lines[0].DebitAccountSource = "Fixed";
        lp.Lines[0].DebitAccountCode = "4.1.01";
        lp.Lines[0].DebitCostCenterSource = DimensionSource.Document;
        lp.Lines[0].DebitItemSource = DimensionSource.Document;
        await db.SaveChangesAsync();

        var entry = await Service(db).PostAsync("650", Ctx(rows: [Row("SOJA", 100m, rule: "R3")]), "u");

        // A ordem das partes é a do expansor; o que importa é cada CC+Item com seu valor e a soma fechando.
        Assert.Equal(100m, entry!.Lines.Sum(l => l.Amount));
        Assert.Equal(
            new[] { ("CC01", (string?)null), ("CC01", "IT01"), ("CC02", null) },
            entry.Lines.Select(l => (l.DebitCostCenterCode!, l.DebitAccountingItemCode)).OrderBy(x => x.Item1).ThenBy(x => x.Item2));
        Assert.All(entry.Lines, l => Assert.InRange(l.Amount, 33.33m, 33.34m));
        Assert.All(entry.Lines, l => Assert.Null(l.CreditCostCenterCode));
    }

    [Fact]
    public async Task Distribution_rounding_residue_goes_to_last_line()
    {
        var db = await SeedAsync();
        await AddRuleAsync(db, Rule("R3", new DateTime(2026, 1, 1), ("CC01", null, 33.33m), ("CC02", null, 33.33m), ("CC01", "IT01", 33.34m)));
        var lp = await db.Context.StandardPostings.Include(p => p.Lines).SingleAsync(p => p.Code == "650");
        lp.Lines[0].DebitAccountSource = "Fixed";
        lp.Lines[0].DebitAccountCode = "4.1.01";
        lp.Lines[0].DebitCostCenterSource = DimensionSource.Document;
        await db.SaveChangesAsync();

        var entry = await Service(db).PostAsync("650", Ctx(rows: [Row("SOJA", 10m, rule: "R3")]), "u");

        Assert.Equal(10m, entry!.Lines.Sum(l => l.Amount));
        Assert.Equal([3.33m, 3.33m, 3.34m], entry.Lines.Select(l => l.Amount).Order());
    }

    [Fact]
    public async Task History_template_replaces_placeholders()
    {
        var entry = await Service(await SeedAsync()).PostAsync("650", Ctx(), "u");
        Assert.Equal("NF 123 SOJA", entry!.Lines[0].History);
    }

    [Fact]
    public async Task External_event_in_closed_period_goes_to_first_open_day()
    {
        var db = await SeedAsync();
        db.Context.AccountingPeriods.Add(new AccountingPeriod
        {
            Year = 2026, Month = 10, StartDate = new DateTime(2026, 10, 1), EndDate = new DateTime(2026, 10, 31),
            Status = AccountingPeriodStatus.Closed,
        });
        await db.SaveChangesAsync();

        var entry = await Service(db).PostAsync("650", Ctx(external: true), "u");

        Assert.Equal(new DateTime(2026, 11, 1), entry!.PostingDate);
        Assert.Contains("(evento de 21/10/2026)", string.Concat(entry.Lines.Select(l => l.History)));
    }

    [Fact]
    public async Task Internal_event_in_closed_period_throws()
    {
        var db = await SeedAsync();
        db.Context.AccountingPeriods.Add(new AccountingPeriod
        {
            Year = 2026, Month = 10, StartDate = new DateTime(2026, 10, 1), EndDate = new DateTime(2026, 10, 31),
            Status = AccountingPeriodStatus.Closed,
        });
        await db.SaveChangesAsync();
        var ex = await Assert.ThrowsAsync<DefaultException>(() => Service(db).PostAsync("650", Ctx(), "u"));
        Assert.Equal("Período contábil 10/2026 fechado.", ex.Message);
    }

    [Fact]
    public async Task Reversal_noop_without_original() =>
        Assert.Null(await Service(await SeedAsync()).PostReversalAsync("655", Ctx("Undo"), "Confirm", "u"));

    [Fact]
    public async Task Reversal_with_active_lp_links_original()
    {
        var db = await SeedAsync();
        var original = await Service(db).PostAsync("650", Ctx(), "u");
        await db.SaveChangesAsync();

        var reversal = await Service(db).PostReversalAsync("655", Ctx("Undo"), "Confirm", "u");
        await db.SaveChangesAsync();

        Assert.Equal("655", reversal!.StandardPostingCode);
        Assert.Equal(original!.DocEntry, reversal.ReversalOfDocEntry);
        Assert.Equal(reversal.DocEntry, (await db.Context.JournalEntries.AsNoTracking().SingleAsync(e => e.DocEntry == original.DocEntry)).ReversedByDocEntry);
        Assert.Equal("2.1.01", reversal.Lines[0].DebitAccountCode);
        Assert.Null(await Service(db).PostReversalAsync("655", Ctx("Undo"), "Confirm", "u"));
    }

    [Fact]
    public async Task Reversal_with_inactive_lp_mirrors_original()
    {
        var db = await SeedAsync();
        var original = await Service(db).PostAsync("650", Ctx(), "u");
        await db.SaveChangesAsync();
        (await db.Context.StandardPostings.SingleAsync(p => p.Code == "655")).Active = false;
        await db.SaveChangesAsync();

        var reversal = await Service(db).PostReversalAsync("655", Ctx("Undo"), "Confirm", "u");

        Assert.Equal(JournalEntryOrigin.Reversal, reversal!.Origin);
        Assert.Equal(original!.Lines[0].CreditAccountCode, reversal.Lines[0].DebitAccountCode);
    }

    [Fact]
    public async Task Reposting_after_reversal_is_allowed()
    {
        var db = await SeedAsync();
        await Service(db).PostAsync("650", Ctx(), "u");
        await db.SaveChangesAsync();
        await Service(db).PostReversalAsync("655", Ctx("Undo"), "Confirm", "u");
        await db.SaveChangesAsync();

        Assert.NotNull(await Service(db).PostAsync("650", Ctx(), "u"));
    }

    [Fact]
    public async Task Does_not_save_changes()
    {
        var name = Guid.NewGuid().ToString();
        var db = await SeedAsync(name);
        await Service(db).PostAsync("650", Ctx(), "u");
        Assert.Single(Added(db));
        Assert.Equal(0, await TestDb.CreateUnitOfWork(name).Context.JournalEntries.CountAsync());
    }
}
