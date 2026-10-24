using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services.Ecd;

namespace SiagroB1.Application.Tests.Ecd;

public class EcdDataLoaderTests
{
    private static int _doc;

    private static JournalEntryLine Dbl(string d, string c, decimal v, string? ccd = null) => new()
    {
        EntryType = JournalEntryLineType.Double, DebitAccountCode = d, CreditAccountCode = c, Amount = v, History = "H",
        DebitCostCenterCode = ccd,
    };

    private static JournalEntry Entry(DateTime date, string branch, params JournalEntryLine[] lines)
    {
        for (var i = 0; i < lines.Length; i++) lines[i].LineNumber = i + 1;
        return new JournalEntry
        {
            PostingDate = date, BranchCode = branch, Batch = "000001", DocumentNumber = (++_doc).ToString("000000"),
            Origin = JournalEntryOrigin.Manual, CreatedBy = "t", Lines = lines.ToList(),
        };
    }

    private static async Task<IUnitOfWork> SeedAsync(params JournalEntry[] entries)
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.Branchs.AddRange(
            new Branch { Code = "02", BranchName = "FILIAL 02", TaxId = "12.345.678/0002-76" },
            new Branch { Code = "01", BranchName = "MATRIZ", TaxId = "12.345.678/0001-95" },
            new Branch { Code = "09", BranchName = "OUTRA", TaxId = "98.765.432/0001-10" });
        db.Context.LedgerAccounts.AddRange(
            new LedgerAccount { Code = "1.1.01", Name = "Caixa", Type = LedgerAccountType.Asset, Nature = LedgerAccountNature.Debit },
            new LedgerAccount { Code = "3.1.01", Name = "Vendas", Type = LedgerAccountType.Revenue, Nature = LedgerAccountNature.Credit },
            new LedgerAccount { Code = "4.1.01", Name = "Despesa", Type = LedgerAccountType.Expense, Nature = LedgerAccountNature.Debit });
        db.Context.JournalEntries.AddRange(entries);
        await db.SaveChangesAsync();
        return db;
    }

    private static EcdRequest Request(string branch = "02") => new() { BranchCode = branch, Year = 2026 };

    [Fact]
    public async Task Load_GroupsRootBranchesAndDeclarantIsHeadOffice()
    {
        var db = await SeedAsync();
        db.Context.EcdSettings.Add(new EcdSettings { BranchCode = "01", BookNumber = 3 });
        db.Context.SpedFiscalSettings.Add(new SpedFiscalSettings { BranchCode = "01" });
        await db.SaveChangesAsync();

        var data = await new EcdDataLoader(db).LoadAsync(Request("02"));

        Assert.Equal(["01", "02"], data.Branches.Select(b => b.Code));
        Assert.Equal("01", data.Declarant?.Code);
        Assert.Equal(3, data.Settings?.BookNumber);
        Assert.Equal("01", data.Accountant?.BranchCode);
        Assert.Equal(3, data.Accounts.Count);
    }

    [Fact]
    public async Task Load_OnlyEntriesOfTheYearFromRootBranches()
    {
        var db = await SeedAsync(
            Entry(new DateTime(2025, 12, 31), "01", Dbl("1.1.01", "3.1.01", 1m)),
            Entry(new DateTime(2026, 1, 1), "01", Dbl("1.1.01", "3.1.01", 2m)),
            Entry(new DateTime(2026, 12, 31), "02", Dbl("1.1.01", "3.1.01", 3m)),
            Entry(new DateTime(2026, 6, 1), "09", Dbl("1.1.01", "3.1.01", 4m)),
            Entry(new DateTime(2027, 1, 1), "01", Dbl("1.1.01", "3.1.01", 5m)));

        var data = await new EcdDataLoader(db).LoadAsync(Request());

        Assert.Equal([2m, 3m], data.Entries.Select(e => e.Lines.Single().Amount));
    }

    [Fact]
    public async Task Load_OpeningBalanceIsEverythingBeforeTheYear()
    {
        var db = await SeedAsync(
            // Saldo inicial na data de corte e movimento do ano anterior, nas duas filiais.
            Entry(new DateTime(2025, 3, 1), "01", Dbl("1.1.01", "3.1.01", 1000m)),
            Entry(new DateTime(2025, 11, 10), "02", Dbl("4.1.01", "1.1.01", 300m)),
            Entry(new DateTime(2025, 11, 10), "09", Dbl("4.1.01", "1.1.01", 77m)),
            Entry(new DateTime(2026, 1, 2), "01", Dbl("1.1.01", "3.1.01", 50m)));

        var data = await new EcdDataLoader(db).LoadAsync(Request());

        Assert.Equal(700m, data.OpeningBalances[("1.1.01", null)]);
        Assert.Equal(-1000m, data.OpeningBalances[("3.1.01", null)]);
        Assert.Equal(300m, data.OpeningBalances[("4.1.01", null)]);
        Assert.Equal(3, data.OpeningBalances.Count);
    }

    [Fact]
    public async Task Load_OpeningBalanceSplitByCostCenter()
    {
        var db = await SeedAsync(
            Entry(new DateTime(2025, 5, 1), "01", Dbl("4.1.01", "1.1.01", 100m, "CC01")),
            Entry(new DateTime(2025, 5, 2), "01", Dbl("4.1.01", "1.1.01", 40m, "CC02")),
            Entry(new DateTime(2025, 5, 3), "01", Dbl("4.1.01", "1.1.01", 5m)));

        var data = await new EcdDataLoader(db).LoadAsync(Request());

        Assert.Equal(100m, data.OpeningBalances[("4.1.01", "CC01")]);
        Assert.Equal(40m, data.OpeningBalances[("4.1.01", "CC02")]);
        Assert.Equal(5m, data.OpeningBalances[("4.1.01", null)]);
        Assert.Equal(-145m, data.OpeningBalances[("1.1.01", null)]);
    }

    [Fact]
    public async Task Load_PeriodsAndViewsOfTheYear()
    {
        var db = await SeedAsync();
        db.Context.AccountingPeriods.AddRange(
            new AccountingPeriod { Year = 2026, Month = 2, Status = AccountingPeriodStatus.Closed },
            new AccountingPeriod { Year = 2026, Month = 1, Status = AccountingPeriodStatus.Closed },
            new AccountingPeriod { Year = 2025, Month = 12, Status = AccountingPeriodStatus.Closed });
        db.Context.ManagementViews.AddRange(
            new ManagementView { Code = "BP", Name = "Balanço", Type = ManagementViewType.BalanceSheet,
                Lines = [new ManagementViewLine { ViewCode = "BP", Code = "1", Description = "Ativo" }] },
            new ManagementView { Code = "DRE", Name = "DRE", Type = ManagementViewType.IncomeStatement });
        await db.SaveChangesAsync();

        var data = await new EcdDataLoader(db).LoadAsync(Request());

        Assert.Equal([1, 2], data.Periods.Select(p => p.Month));
        Assert.Equal("BP", data.BalanceSheet?.Code);
        Assert.Single(data.BalanceSheet!.Lines);
        Assert.Equal("DRE", data.IncomeStatement?.Code);
    }
}
