using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Tests.Financials;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using static SiagroB1.Application.Tests.Accounting.Postings.AccountingPostingServiceTests;

namespace SiagroB1.Application.Tests.Accounting.Postings;

/// <summary>Documento de Saída: 610/620 na confirmação; 630/635 no estorno da confirmação e no cancelamento.</summary>
public class SalesInvoicePostingTests
{
    private static async Task<SalesFirmReceivableSeed> SeedAsync()
    {
        var seed = await SalesFirmReceivableSeed.CreateAsync();
        seed.Context.LedgerAccounts.AddRange(
            new LedgerAccount { Code = "1.1.02", Name = "Clientes", Type = LedgerAccountType.Asset },
            new LedgerAccount { Code = "3.1.01", Name = "Receita", Type = LedgerAccountType.Revenue },
            new LedgerAccount { Code = "3.2.01", Name = "ICMS s/ vendas", Type = LedgerAccountType.Expense },
            new LedgerAccount { Code = "2.1.05", Name = "ICMS a recolher", Type = LedgerAccountType.Liability });
        seed.Context.AccountingSettings.Add(new AccountingSettings { CutoverDate = new DateTime(2026, 1, 1), UpdatedBy = "t" });
        seed.Context.StandardPostings.AddRange(
            Lp("610", Seq("001", "Fixed", "1.1.02", "Fixed", "3.1.01", "Line.GrandTotal", "Venda NF {DocNumber}")),
            Lp("620", Seq("001", "Fixed", "3.2.01", "Fixed", "2.1.05", "Document.GrandTotal", "Total NF {DocNumber}")),
            Lp("630", Seq("001", "Fixed", "3.1.01", "Fixed", "1.1.02", "Line.GrandTotal", "Estorno NF {DocNumber}")));
        await seed.Context.SaveChangesAsync();
        return seed;
    }

    private static async Task<List<JournalEntry>> Entries(SalesFirmReceivableSeed seed) =>
        await seed.Context.JournalEntries.AsNoTracking().Include(e => e.Lines).OrderBy(e => e.DocEntry).ToListAsync();

    [Fact]
    public async Task Confirm_posts_610_and_620()
    {
        var seed = await SeedAsync();
        var (invoice, _) = await seed.AddInvoiceAsync();

        await seed.Confirm(accounting: Service(seed.Db)).ExecuteAsync(invoice.Key, "ana");

        var entries = await Entries(seed);
        var e610 = entries.Single(e => e.StandardPostingCode == "610");
        Assert.Equal(90_000m, e610.Lines.Single().Amount);
        Assert.Equal("Venda NF 7001", e610.Lines.Single().History);
        Assert.Equal(new DateTime(2026, 10, 15), e610.PostingDate);
        Assert.Equal(90_000m, entries.Single(e => e.StandardPostingCode == "620").Lines.Single().Amount);
    }

    [Fact]
    public async Task Reverse_confirm_posts_630_and_auto_reverses_620()
    {
        var seed = await SeedAsync();
        var (invoice, _) = await seed.AddInvoiceAsync();
        await seed.Confirm(accounting: Service(seed.Db)).ExecuteAsync(invoice.Key, "ana");

        await seed.ReverseConfirm(accounting: Service(seed.Db)).ExecuteAsync(invoice.Key, "ana");

        var entries = await Entries(seed);
        Assert.Equal(["610", "620", "630", "620"], entries.Select(e => e.StandardPostingCode));
        Assert.Equal(JournalEntryOrigin.Reversal, entries[3].Origin); // 635 não configurado: espelho do 620
        Assert.All(entries.Take(2), e => Assert.NotNull(e.ReversedByDocEntry));
    }

    [Fact]
    public async Task Cancel_of_confirmed_invoice_reverses()
    {
        var seed = await SeedAsync();
        var (invoice, _) = await seed.AddInvoiceAsync();
        await seed.Confirm(accounting: Service(seed.Db)).ExecuteAsync(invoice.Key, "ana");

        await seed.Cancel(accounting: Service(seed.Db)).ExecuteAsync(invoice.Key, "ana");

        Assert.Equal(["610", "620", "630", "620"], (await Entries(seed)).Select(e => e.StandardPostingCode));
    }

    [Fact]
    public async Task Nfe_cancellation_in_closed_month_posts_on_first_open_day()
    {
        var seed = await SeedAsync();
        var (invoice, _) = await seed.AddInvoiceAsync();
        await seed.Confirm(accounting: Service(seed.Db)).ExecuteAsync(invoice.Key, "ana");
        var tracked = await seed.Context.SalesInvoices.SingleAsync(i => i.Key == invoice.Key);
        tracked.NfeStatus = NfeStatus.Cancelled;
        var today = DateTime.Today;
        seed.Context.AccountingPeriods.Add(new AccountingPeriod
        {
            Year = today.Year, Month = today.Month, StartDate = new DateTime(today.Year, today.Month, 1),
            EndDate = new DateTime(today.Year, today.Month, 1).AddMonths(1).AddDays(-1), Status = AccountingPeriodStatus.Closed,
        });
        await seed.Context.SaveChangesAsync();

        await seed.Cancel(accounting: Service(seed.Db)).CancelAfterNfeAsync(invoice.Key, "ana");

        var reversal = (await Entries(seed)).Single(e => e.StandardPostingCode == "630");
        Assert.Equal(new DateTime(today.Year, today.Month, 1).AddMonths(1), reversal.PostingDate);
    }
}
