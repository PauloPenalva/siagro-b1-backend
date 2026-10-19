using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Financials;

public class FinancialAccountEntriesServiceTests
{
    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();

    private FinancialAccountEntriesCreateService Creator() => new(_db);
    private FinancialAccountEntriesDeleteService Deleter() => new(_db);

    private async Task SeedAccountAsync(bool inactive = false)
    {
        _db.Context.FinancialAccounts.Add(new FinancialAccount
        {
            Code = "BCO1", Name = "Banco", Type = FinancialAccountType.Bank, Currency = CurrencyType.Brl, Inactive = inactive
        });
        await _db.Context.SaveChangesAsync();
    }

    private static FinancialAccountEntry Entry(decimal amount = -12.5m, string description = "Tarifa") => new()
    {
        FinancialAccountCode = "BCO1", EntryDate = new DateTime(2026, 10, 5, 14, 30, 0), Amount = amount, Description = description
    };

    [Fact]
    public async Task Creates_the_entry_with_date_only_and_rounded_amount()
    {
        await SeedAccountAsync();

        var created = await Creator().ExecuteAsync(Entry(amount: -12.499m), "tester");

        var stored = await _db.Context.FinancialAccountEntries.SingleAsync();
        Assert.Equal(created.Key, stored.Key);
        Assert.Equal(new DateTime(2026, 10, 5), stored.EntryDate);
        Assert.Equal(-12.50m, stored.Amount);
        Assert.Equal("tester", stored.CreatedBy);
    }

    [Fact]
    public async Task Refuses_zero_amount()
    {
        await SeedAccountAsync();
        var e = await Assert.ThrowsAsync<ApplicationException>(() => Creator().ExecuteAsync(Entry(amount: 0m), "t"));
        Assert.Equal("Informe um valor diferente de zero.", e.Message);
    }

    [Fact]
    public async Task Refuses_inactive_account()
    {
        await SeedAccountAsync(inactive: true);
        var e = await Assert.ThrowsAsync<ApplicationException>(() => Creator().ExecuteAsync(Entry(), "t"));
        Assert.Equal("A conta financeira está inativa.", e.Message);
    }

    [Fact]
    public async Task Refuses_unknown_account()
    {
        var e = await Assert.ThrowsAsync<ApplicationException>(() => Creator().ExecuteAsync(Entry(), "t"));
        Assert.Equal("Conta financeira BCO1 não encontrada.", e.Message);
    }

    [Fact]
    public async Task Refuses_blank_description()
    {
        await SeedAccountAsync();
        var e = await Assert.ThrowsAsync<ApplicationException>(() => Creator().ExecuteAsync(Entry(description: "  "), "t"));
        Assert.Equal("Informe a descrição do lançamento.", e.Message);
    }

    [Fact]
    public async Task Deletes_an_unreconciled_entry()
    {
        await SeedAccountAsync();
        var created = await Creator().ExecuteAsync(Entry(), "t");

        await Deleter().ExecuteAsync(created.Key);

        Assert.False(await _db.Context.FinancialAccountEntries.AnyAsync());
    }

    [Fact]
    public async Task Refuses_to_delete_a_reconciled_entry()
    {
        await SeedAccountAsync();
        var created = await Creator().ExecuteAsync(Entry(), "t");
        created.ReconciliationKey = Guid.NewGuid();
        await _db.Context.SaveChangesAsync();

        var e = await Assert.ThrowsAsync<ApplicationException>(() => Deleter().ExecuteAsync(created.Key));
        Assert.Equal("Lançamento conciliado não pode ser excluído. Remova a conciliação primeiro.", e.Message);
    }
}
