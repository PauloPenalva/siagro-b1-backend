using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Financials;

public class FinancialAccountTransfersServiceTests
{
    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();

    private FinancialAccountTransfersCreateService Creator() => new(_db);
    private FinancialAccountTransfersDeleteService Deleter() => new(_db);

    private async Task SeedAsync(CurrencyType toCurrency = CurrencyType.Brl, bool fromInactive = false,
        bool toInactive = false, string toBranch = "")
    {
        _db.Context.FinancialAccounts.AddRange(
            new FinancialAccount { Code = "BCO1", Name = "Banco", Type = FinancialAccountType.Bank, Currency = CurrencyType.Brl, Inactive = fromInactive },
            new FinancialAccount { Code = "CX1", Name = "Caixa", Type = FinancialAccountType.Bank, Currency = toCurrency, Inactive = toInactive, BranchCode = toBranch });
        await _db.Context.SaveChangesAsync();
    }

    private static FinancialAccountTransfer Transfer(decimal amount = 1000m, string from = "BCO1", string to = "CX1",
        string description = "") => new()
    {
        FromAccountCode = from, ToAccountCode = to, TransferDate = new DateTime(2026, 10, 5, 14, 30, 0),
        Amount = amount, Description = description
    };

    [Fact]
    public async Task Creates_transfer_with_two_entries_on_same_date()
    {
        await SeedAsync();
        var created = await Creator().ExecuteAsync(Transfer(), "tester");

        Assert.Single(await _db.Context.FinancialAccountTransfers.ToListAsync());
        var entries = await _db.Context.FinancialAccountEntries.ToListAsync();
        Assert.Equal(2, entries.Count);
        var from = entries.Single(x => x.FinancialAccountCode == "BCO1");
        var to = entries.Single(x => x.FinancialAccountCode == "CX1");
        Assert.Equal(-1000m, from.Amount);
        Assert.Equal(1000m, to.Amount);
        Assert.Equal(new DateTime(2026, 10, 5), from.EntryDate);
        Assert.Equal(new DateTime(2026, 10, 5), to.EntryDate);
        Assert.Equal(created.Key, from.TransferKey);
        Assert.Equal(created.Key, to.TransferKey);
        Assert.Equal("Transferência para CX1", from.Description);
        Assert.Equal("Transferência de BCO1", to.Description);
    }

    [Fact]
    public async Task Appends_description_to_entries_when_given()
    {
        await SeedAsync();
        await Creator().ExecuteAsync(Transfer(description: "Reforço de caixa"), "t");
        var entries = await _db.Context.FinancialAccountEntries.ToListAsync();
        Assert.Contains(entries, x => x.Description == "Transferência para CX1 - Reforço de caixa");
        Assert.Contains(entries, x => x.Description == "Transferência de BCO1 - Reforço de caixa");
    }

    [Fact]
    public async Task Rejects_same_account()
    {
        await SeedAsync();
        var e = await Assert.ThrowsAsync<ApplicationException>(() => Creator().ExecuteAsync(Transfer(to: "bco1"), "t"));
        Assert.Equal("A conta de origem e a de destino precisam ser diferentes.", e.Message);
    }

    [Fact]
    public async Task Rejects_different_currency()
    {
        await SeedAsync(toCurrency: CurrencyType.Usd);
        var e = await Assert.ThrowsAsync<ApplicationException>(() => Creator().ExecuteAsync(Transfer(), "t"));
        Assert.Contains("mesma moeda", e.Message);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Rejects_inactive_account(bool fromInactive, bool toInactive)
    {
        await SeedAsync(fromInactive: fromInactive, toInactive: toInactive);
        var e = await Assert.ThrowsAsync<ApplicationException>(() => Creator().ExecuteAsync(Transfer(), "t"));
        Assert.Contains("inativa", e.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Rejects_zero_or_negative_amount(int amount)
    {
        await SeedAsync();
        var e = await Assert.ThrowsAsync<ApplicationException>(() => Creator().ExecuteAsync(Transfer(amount), "t"));
        Assert.Contains("maior que zero", e.Message);
    }

    [Fact]
    public async Task Allows_accounts_of_different_branches()
    {
        await SeedAsync(toBranch: "02");
        await Creator().ExecuteAsync(Transfer(), "t");
        Assert.Equal(2, await _db.Context.FinancialAccountEntries.CountAsync());
    }

    [Fact]
    public async Task Delete_removes_transfer_and_both_entries()
    {
        await SeedAsync();
        var created = await Creator().ExecuteAsync(Transfer(), "t");
        await Deleter().ExecuteAsync(created.Key);
        Assert.Empty(await _db.Context.FinancialAccountTransfers.ToListAsync());
        Assert.Empty(await _db.Context.FinancialAccountEntries.ToListAsync());
    }

    [Fact]
    public async Task Delete_blocked_when_any_entry_reconciled()
    {
        await SeedAsync();
        var created = await Creator().ExecuteAsync(Transfer(), "t");
        var leg = await _db.Context.FinancialAccountEntries.SingleAsync(x => x.FinancialAccountCode == "CX1");
        leg.ReconciliationKey = Guid.NewGuid();
        await _db.Context.SaveChangesAsync();

        var e = await Assert.ThrowsAsync<ApplicationException>(() => Deleter().ExecuteAsync(created.Key));
        Assert.Contains("conciliad", e.Message);
        Assert.Equal(2, await _db.Context.FinancialAccountEntries.CountAsync());
    }

    [Fact]
    public async Task Entry_delete_rejects_transfer_leg()
    {
        await SeedAsync();
        await Creator().ExecuteAsync(Transfer(), "t");
        var leg = await _db.Context.FinancialAccountEntries.FirstAsync();
        var e = await Assert.ThrowsAsync<ApplicationException>(() =>
            new FinancialAccountEntriesDeleteService(_db).ExecuteAsync(leg.Key));
        Assert.Equal("Lançamento de transferência: exclua pela transferência.", e.Message);
    }
}
