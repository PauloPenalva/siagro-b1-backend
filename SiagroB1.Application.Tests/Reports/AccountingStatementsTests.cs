using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;
using SiagroB1.Reports.Services.Accounting;

namespace SiagroB1.Application.Tests.Reports;

/// <summary>Demonstrações pela visão gerencial (DRE, Balanço) e o Diário.</summary>
public class AccountingStatementsTests
{
    private static int _doc;

    public static JournalEntryLine Dbl(string d, string c, decimal v, string? ccd = null, string h = "H") => new()
    {
        EntryType = JournalEntryLineType.Double, DebitAccountCode = d, CreditAccountCode = c, Amount = v, History = h,
        DebitCostCenterCode = ccd,
    };

    public static JournalEntry Entry(DateTime date, params JournalEntryLine[] lines) =>
        Entry(date, JournalEntryOrigin.Manual, null, lines);

    public static JournalEntry Entry(DateTime date, JournalEntryOrigin origin, string? sourceType, params JournalEntryLine[] lines)
    {
        for (var i = 0; i < lines.Length; i++) lines[i].LineNumber = i + 1;
        return new JournalEntry
        {
            PostingDate = date, BranchCode = "01", Batch = origin == JournalEntryOrigin.ResultClosing ? "008860" : "008890",
            DocumentNumber = (++_doc).ToString("000000"), Origin = origin, SourceDocumentType = sourceType, CreatedBy = "t",
            Lines = lines.ToList(),
        };
    }

    /// <summary>
    /// Plano: 1.1.01 Caixa, 2.1.01 Fornecedores, 2.3.01 Capital, 2.3.09 Apuração; 3.1.01 Vendas, 3.2.01 Devoluções
    /// (dedução, devedora), 4.1.01 CMV, 4.2.01 Despesas. Movimento: capital 1.000 em set; outubro vendas 1.000,
    /// devoluções 100, CMV 400, despesas 200 (CC01 150, sem centro 50); janeiro vendas 500 (acumulado).
    /// </summary>
    public static async Task<IUnitOfWork> SeedAsync()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.LedgerAccounts.AddRange(
            new LedgerAccount { Code = "1.1.01", Name = "Caixa", Type = LedgerAccountType.Asset, Nature = LedgerAccountNature.Debit },
            new LedgerAccount { Code = "2.1.01", Name = "Fornecedores", Type = LedgerAccountType.Liability, Nature = LedgerAccountNature.Credit },
            new LedgerAccount { Code = "2.3.01", Name = "Capital", Type = LedgerAccountType.Equity, Nature = LedgerAccountNature.Credit },
            new LedgerAccount { Code = "2.3.09", Name = "Apuração", Type = LedgerAccountType.Equity, Nature = LedgerAccountNature.Credit },
            new LedgerAccount { Code = "3.1.01", Name = "Vendas", Type = LedgerAccountType.Revenue, Nature = LedgerAccountNature.Credit },
            new LedgerAccount { Code = "3.2.01", Name = "Devoluções", Type = LedgerAccountType.Revenue, Nature = LedgerAccountNature.Debit },
            new LedgerAccount { Code = "4.1.01", Name = "CMV", Type = LedgerAccountType.Expense, Nature = LedgerAccountNature.Debit },
            new LedgerAccount { Code = "4.2.01", Name = "Despesas", Type = LedgerAccountType.Expense, Nature = LedgerAccountNature.Debit });
        db.Context.JournalEntries.AddRange(
            Entry(new DateTime(2026, 9, 1), Dbl("1.1.01", "2.3.01", 1000m)),
            Entry(new DateTime(2026, 1, 15), Dbl("1.1.01", "3.1.01", 500m)),
            Entry(new DateTime(2026, 10, 5), Dbl("1.1.01", "3.1.01", 1000m)),
            Entry(new DateTime(2026, 10, 6), Dbl("3.2.01", "1.1.01", 100m)),
            Entry(new DateTime(2026, 10, 7), Dbl("4.1.01", "2.1.01", 400m)),
            Entry(new DateTime(2026, 10, 8), Dbl("4.2.01", "1.1.01", 150m, ccd: "CC01")),
            Entry(new DateTime(2026, 10, 9), Dbl("4.2.01", "1.1.01", 50m)));
        db.Context.ManagementViews.Add(Dre());
        db.Context.ManagementViews.Add(Bp());
        await db.SaveChangesAsync();
        return db;
    }

    private static ManagementViewLine L(int order, string code, ManagementViewLineKind kind, string? from = null,
        string? to = null, string? formula = null, string? parent = null, bool minus = false) => new()
    {
        Order = order, Code = code, Description = $"Linha {code}", Kind = kind, AccountFrom = from, AccountTo = to,
        Formula = formula, ParentCode = parent, Sign = minus ? ManagementViewLineSign.Minus : ManagementViewLineSign.Plus,
    };

    private static ManagementView Dre() => new()
    {
        Code = "DRE", Name = "DRE", Type = ManagementViewType.IncomeStatement, UpdatedBy = "t",
        Lines =
        [
            L(1, "01", ManagementViewLineKind.Accounts, "3.1", "3.1.99"),
            L(2, "02", ManagementViewLineKind.Accounts, "3.2", "3.2.99"),
            L(3, "03", ManagementViewLineKind.Formula, formula: "01 - 02"),
            L(4, "04", ManagementViewLineKind.Accounts, "4.1", "4.1.99"),
            L(5, "05", ManagementViewLineKind.Formula, formula: "03 - 04"),
            L(6, "06", ManagementViewLineKind.Accounts, "4.2", "4.2.99"),
            L(7, "10", ManagementViewLineKind.Formula, formula: "05 - 06"),
        ],
    };

    private static ManagementView Bp() => new()
    {
        Code = "BP", Name = "BP", Type = ManagementViewType.BalanceSheet, UpdatedBy = "t",
        Lines =
        [
            L(1, "1", ManagementViewLineKind.Total),
            L(2, "1.1", ManagementViewLineKind.Accounts, "1", "1.9", parent: "1"),
            L(3, "2", ManagementViewLineKind.Total),
            L(4, "2.1", ManagementViewLineKind.Accounts, "2.1", "2.2.99", parent: "2"),
            L(5, "2.3", ManagementViewLineKind.Accounts, "2.3", "2.3.99", parent: "2"),
        ],
    };

    private static IncomeStatementReportRequest October(bool byCc = false) => new()
    {
        DateFrom = new DateTime(2026, 10, 1), DateTo = new DateTime(2026, 10, 31), ViewCode = "DRE", ByCostCenter = byCc,
    };

    // ---------- calculador ----------

    private static readonly Dictionary<string, AccountInfo> Accounts = new()
    {
        ["3.1.01"] = new("3.1.01", true, LedgerAccountNature.Credit, LedgerAccountType.Revenue),
        ["4.1.01"] = new("4.1.01", true, LedgerAccountNature.Debit, LedgerAccountType.Expense),
        ["4.1"] = new("4.1", false, LedgerAccountNature.Debit, LedgerAccountType.Expense),
    };

    [Fact]
    public void Calculator_signs_revenue_positive_and_expense_positive()
    {
        var values = ManagementViewCalculator.Compute(
            [L(1, "R", ManagementViewLineKind.Accounts, "3", "3.9"), L(2, "D", ManagementViewLineKind.Accounts, "4", "4.9")],
            Accounts, code => code == "3.1.01" ? -1000m : code == "4.1.01" ? 400m : 999m);

        Assert.Equal(1000m, values["R"]);
        Assert.Equal(400m, values["D"]); // só a analítica: a sintética 4.1 (999) não conta
    }

    [Fact]
    public void Calculator_total_sums_children_with_sign()
    {
        var values = ManagementViewCalculator.Compute(
        [
            L(1, "T", ManagementViewLineKind.Total),
            L(2, "R", ManagementViewLineKind.Accounts, "3", "3.9", parent: "T"),
            L(3, "D", ManagementViewLineKind.Accounts, "4", "4.9", parent: "T", minus: true),
        ], Accounts, code => code == "3.1.01" ? -1000m : 400m);

        Assert.Equal(600m, values["T"]);
        Assert.Equal(400m, values["D"]);
    }

    [Fact]
    public void Calculator_formula_resolves_in_any_order()
    {
        var values = ManagementViewCalculator.Compute(
        [
            L(1, "F", ManagementViewLineKind.Formula, formula: "R - D"),
            L(2, "R", ManagementViewLineKind.Accounts, "3", "3.9"),
            L(3, "D", ManagementViewLineKind.Accounts, "4", "4.9"),
        ], Accounts, code => code == "3.1.01" ? -1000m : 400m);

        Assert.Equal(600m, values["F"]);
    }

    [Fact]
    public void Calculator_empty_range_is_zero()
    {
        var values = ManagementViewCalculator.Compute([L(1, "X", ManagementViewLineKind.Accounts)], Accounts, _ => 5m);
        Assert.Equal(0m, values["X"]);
    }

    // ---------- DRE ----------

    [Fact]
    public async Task Income_statement_computes_period_lines()
    {
        var rows = await new IncomeStatementReportService(await SeedAsync(), null!).RowsAsync(October());

        Assert.Equal(1000m, rows.Single(r => r.Code == "01").Period);
        Assert.Equal(100m, rows.Single(r => r.Code == "02").Period);
        Assert.Equal(900m, rows.Single(r => r.Code == "03").Period);
        Assert.Equal(500m, rows.Single(r => r.Code == "05").Period);
        Assert.Equal(300m, rows.Single(r => r.Code == "10").Period);
    }

    [Fact] // [RF 3]
    public async Task Income_statement_ignores_result_closing()
    {
        var db = await SeedAsync();
        db.Context.JournalEntries.Add(Entry(new DateTime(2026, 10, 31), JournalEntryOrigin.ResultClosing, "ResultClosing",
            Dbl("3.1.01", "2.3.09", 1500m)));
        db.Context.JournalEntries.Add(Entry(new DateTime(2026, 10, 31), JournalEntryOrigin.Reversal, "ResultClosing",
            Dbl("2.3.09", "3.1.01", 10m)));
        await db.SaveChangesAsync();

        var rows = await new IncomeStatementReportService(db, null!).RowsAsync(October());

        Assert.Equal(1000m, rows.Single(r => r.Code == "01").Period);
    }

    [Fact]
    public async Task Income_statement_year_to_date_and_percent()
    {
        var rows = await new IncomeStatementReportService(await SeedAsync(), null!).RowsAsync(October());

        var revenue = rows.Single(r => r.Code == "01");
        Assert.Equal(1500m, revenue.YearToDate);
        Assert.Equal(100m, revenue.PercentOfRevenue);
        Assert.Equal(30m, rows.Single(r => r.Code == "10").PercentOfRevenue);
    }

    [Fact]
    public async Task Income_statement_by_cost_center_has_no_center_column()
    {
        var rows = await new IncomeStatementReportService(await SeedAsync(), null!).RowsAsync(October(byCc: true));

        var expenses = rows.Single(r => r.Code == "06");
        Assert.Equal(150m, expenses.ByCostCenter!["CC01"]);
        Assert.Equal(50m, expenses.ByCostCenter![IncomeStatementReportService.NoCenter]);
        Assert.Equal(200m, expenses.Period);
    }

    [Fact]
    public async Task Income_statement_unknown_view_is_refused()
    {
        var db = await SeedAsync();
        var e = await Assert.ThrowsAsync<ApplicationException>(() =>
            new IncomeStatementReportService(db, null!).RowsAsync(new IncomeStatementReportRequest
            {
                DateFrom = new DateTime(2026, 10, 1), DateTo = new DateTime(2026, 10, 31), ViewCode = "XX",
            }));
        Assert.Equal("Visão gerencial XX não encontrada.", e.Message);
    }
}
