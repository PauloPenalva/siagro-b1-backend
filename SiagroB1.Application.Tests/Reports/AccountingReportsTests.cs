using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;

namespace SiagroB1.Application.Tests.Reports;

/// <summary>Razão e Balancete: saldos calculados das linhas de lançamento (sem tabela de saldo).</summary>
public class AccountingReportsTests
{
    private static readonly DateTime Oct1 = new(2026, 10, 1);

    private static JournalEntryLine Dbl(string d, string c, decimal v, string h = "H", string? ccd = null) => new()
    {
        EntryType = JournalEntryLineType.Double, DebitAccountCode = d, CreditAccountCode = c, Amount = v, History = h,
        DebitCostCenterCode = ccd,
    };

    private static int _doc;

    private static JournalEntry Entry(DateTime date, string branch, params JournalEntryLine[] lines)
    {
        for (var i = 0; i < lines.Length; i++) lines[i].LineNumber = i + 1;
        return new JournalEntry
        {
            PostingDate = date, BranchCode = branch, Batch = "008890", DocumentNumber = (++_doc).ToString("000000"),
            Origin = JournalEntryOrigin.Manual, CreatedBy = "t", Lines = lines.ToList(),
        };
    }

    private static async Task<IUnitOfWork> SeedAsync()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.LedgerAccounts.AddRange(
            new LedgerAccount { Code = "1", Name = "Ativo", Type = LedgerAccountType.Asset, AllowsPosting = false, Nature = LedgerAccountNature.Debit },
            new LedgerAccount { Code = "1.1", Name = "Circulante", Type = LedgerAccountType.Asset, AllowsPosting = false, ParentCode = "1", Nature = LedgerAccountNature.Debit },
            new LedgerAccount { Code = "1.1.01", Name = "Caixa", Type = LedgerAccountType.Asset, ParentCode = "1.1", Nature = LedgerAccountNature.Debit },
            new LedgerAccount { Code = "1.1.02", Name = "Banco", Type = LedgerAccountType.Asset, ParentCode = "1.1", Nature = LedgerAccountNature.Debit },
            new LedgerAccount { Code = "3", Name = "Receitas", Type = LedgerAccountType.Revenue, AllowsPosting = false, Nature = LedgerAccountNature.Credit },
            new LedgerAccount { Code = "3.1.01", Name = "Vendas", Type = LedgerAccountType.Revenue, ParentCode = "3", Nature = LedgerAccountNature.Credit },
            new LedgerAccount { Code = "4.1.01", Name = "Despesa", Type = LedgerAccountType.Expense, Nature = LedgerAccountNature.Debit });
        db.Context.JournalEntries.AddRange(
            // Abertura antes do período.
            Entry(new DateTime(2026, 9, 30), "01", Dbl("1.1.01", "3.1.01", 1000m, "Abertura")),
            // Movimento do período.
            Entry(new DateTime(2026, 10, 5), "01", Dbl("1.1.02", "1.1.01", 300m, "Depósito")),
            Entry(new DateTime(2026, 10, 9), "01",
                new JournalEntryLine { EntryType = JournalEntryLineType.Debit, DebitAccountCode = "4.1.01", Amount = 50m, History = "Tarifa", DebitCostCenterCode = "CC01" },
                new JournalEntryLine { EntryType = JournalEntryLineType.HistoryContinuation, History = " bancária" },
                new JournalEntryLine { EntryType = JournalEntryLineType.Credit, CreditAccountCode = "1.1.02", Amount = 50m, History = "Tarifa" }),
            Entry(new DateTime(2026, 10, 20), "02", Dbl("1.1.01", "3.1.01", 70m, "Venda filial 2")),
            // Depois do período.
            Entry(new DateTime(2026, 11, 2), "01", Dbl("1.1.01", "3.1.01", 9m)));
        await db.SaveChangesAsync();
        return db;
    }

    private static LedgerReportRequest Ledger(string? from = null, string? to = null, string? branch = null, string? cc = null) => new()
    {
        DateFrom = Oct1, DateTo = new DateTime(2026, 10, 31), AccountFrom = from, AccountTo = to, BranchCode = branch, CostCenterCode = cc,
    };

    [Fact]
    public async Task Ledger_opening_includes_everything_before_start()
    {
        var rows = await new LedgerReportService(await SeedAsync(), new RecordingFastReportService()).RowsAsync(Ledger("1.1.01", "1.1.01"));
        var opening = rows.First();
        Assert.Equal("Saldo anterior", opening.History);
        Assert.Equal(1000m, opening.Balance);
        Assert.Equal("D", opening.BalanceSide);
    }

    [Fact]
    public async Task Ledger_running_balance_with_side()
    {
        var rows = await new LedgerReportService(await SeedAsync(), new RecordingFastReportService()).RowsAsync(Ledger("1.1.01", "1.1.01"));
        // 1000 D - 300 (crédito) + 70 (débito filial 02) = 770 D
        Assert.Equal([1000m, 700m, 770m], rows.Select(r => r.Balance));
        Assert.All(rows, r => Assert.Equal("D", r.BalanceSide));

        var revenue = await new LedgerReportService(await SeedAsync(), new RecordingFastReportService()).RowsAsync(Ledger("3.1.01", "3.1.01"));
        Assert.Equal("C", revenue.Last().BalanceSide);
        Assert.Equal(1070m, revenue.Last().Balance);
    }

    [Fact]
    public async Task Ledger_double_entry_line_shows_counterpart()
    {
        var rows = await new LedgerReportService(await SeedAsync(), new RecordingFastReportService()).RowsAsync(Ledger("1.1.02", "1.1.02"));
        var deposit = rows.Single(r => r.History == "Depósito");
        Assert.Equal("1.1.01", deposit.Counterpart);
        Assert.Equal(300m, deposit.Debit);
        var fee = rows.Single(r => r.Credit == 50m);
        Assert.Equal(string.Empty, fee.Counterpart);
    }

    [Fact]
    public async Task Ledger_joins_history_continuation()
    {
        var rows = await new LedgerReportService(await SeedAsync(), new RecordingFastReportService()).RowsAsync(Ledger("4.1.01", "4.1.01"));
        Assert.Equal("Tarifa bancária", rows.Single(r => r.Debit == 50m).History);
    }

    [Fact]
    public async Task Ledger_filters_by_branch_and_cost_center()
    {
        var svc = new LedgerReportService(await SeedAsync(), new RecordingFastReportService());
        var branch1 = await svc.RowsAsync(Ledger("1.1.01", "1.1.01", branch: "01"));
        Assert.Equal(700m, branch1.Last().Balance);

        var cc = await svc.RowsAsync(Ledger(cc: "CC01"));
        Assert.All(cc.Where(r => r.History != "Saldo anterior"), r => Assert.Equal("4.1.01", r.AccountCode));
    }

    [Fact]
    public async Task Synthetic_account_range_includes_its_descendants()
    {
        var db = await SeedAsync();
        var ledger = await new LedgerReportService(db, new RecordingFastReportService()).RowsAsync(Ledger("1", "1"));
        Assert.Equal(["1.1.01", "1.1.02"], ledger.Select(r => r.AccountCode).Distinct());

        var request = Trial();
        request.AccountFrom = "1.1";
        request.AccountTo = "1.1";
        var trial = await new TrialBalanceReportService(db, new RecordingFastReportService()).RowsAsync(request);
        Assert.Equal(["1", "1.1", "1.1.01", "1.1.02"], trial.Select(r => r.AccountCode));
    }

    [Fact]
    public async Task Ledger_skips_accounts_without_balance_or_movement()
    {
        var rows = await new LedgerReportService(await SeedAsync(), new RecordingFastReportService()).RowsAsync(Ledger());
        Assert.Equal(["1.1.01", "1.1.02", "3.1.01", "4.1.01"], rows.Select(r => r.AccountCode).Distinct());
    }

    [Fact]
    public async Task Ledger_pdf_uses_template()
    {
        var recorder = new RecordingFastReportService();
        await new LedgerReportService(await SeedAsync(), recorder).ExecuteAsync(Ledger());
        Assert.Equal("AccountingLedger.frx", recorder.LastReportName);
        Assert.Equal("Período: 01/10/2026 a 31/10/2026", recorder.LastParameters!["pPeriod"]);
    }

    private static TrialBalanceReportRequest Trial(int? level = null, bool onlyWithMovement = false) => new()
    {
        DateFrom = Oct1, DateTo = new DateTime(2026, 10, 31), Level = level, OnlyWithMovement = onlyWithMovement,
    };

    [Fact]
    public async Task TrialBalance_totals_debit_equals_credit()
    {
        var rows = await new TrialBalanceReportService(await SeedAsync(), new RecordingFastReportService()).RowsAsync(Trial());
        var analytic = rows.Where(r => r.Analytic).ToList();
        Assert.Equal(analytic.Sum(r => r.Debit), analytic.Sum(r => r.Credit));
        Assert.Equal(420m, analytic.Sum(r => r.Debit));
    }

    [Fact]
    public async Task TrialBalance_synthetic_sums_children_by_parent()
    {
        var rows = await new TrialBalanceReportService(await SeedAsync(), new RecordingFastReportService()).RowsAsync(Trial());
        var circ = rows.Single(r => r.AccountCode == "1.1");
        Assert.Equal(1000m, circ.Opening);
        Assert.Equal(370m, circ.Debit);   // 300 banco + 70 caixa
        Assert.Equal(350m, circ.Credit);  // 300 caixa + 50 banco
        Assert.Equal(1020m, circ.Closing);
        Assert.Equal("D", circ.ClosingSide);
        Assert.Equal(1020m, rows.Single(r => r.AccountCode == "1").Closing);
        Assert.Equal(["1", "1.1", "1.1.01", "1.1.02", "3", "3.1.01", "4.1.01"], rows.Select(r => r.AccountCode));
    }

    [Fact]
    public async Task TrialBalance_level_filter()
    {
        var rows = await new TrialBalanceReportService(await SeedAsync(), new RecordingFastReportService()).RowsAsync(Trial(level: 2));
        // Nível = profundidade na hierarquia pela conta superior (4.1.01 não tem superior: nível 1).
        Assert.Equal(["1", "1.1", "3", "3.1.01", "4.1.01"], rows.Select(r => r.AccountCode));
    }

    [Fact]
    public async Task TrialBalance_hides_accounts_without_balance_or_movement()
    {
        var db = await SeedAsync();
        db.Context.LedgerAccounts.Add(new LedgerAccount { Code = "1.1.03", Name = "Parado", Type = LedgerAccountType.Asset, ParentCode = "1.1" });
        await db.SaveChangesAsync();
        var all = await new TrialBalanceReportService(db, new RecordingFastReportService()).RowsAsync(Trial());
        Assert.DoesNotContain(all, r => r.AccountCode == "1.1.03");
    }

    [Fact]
    public async Task TrialBalance_only_with_movement_hides_accounts_with_opening_only()
    {
        var db = await SeedAsync();
        var request = Trial(onlyWithMovement: true);
        request.DateFrom = new DateTime(2026, 11, 1);
        request.DateTo = new DateTime(2026, 11, 30);
        var rows = await new TrialBalanceReportService(db, new RecordingFastReportService()).RowsAsync(request);
        Assert.Equal(["1", "1.1", "1.1.01", "3", "3.1.01"], rows.Select(r => r.AccountCode));
    }
}
