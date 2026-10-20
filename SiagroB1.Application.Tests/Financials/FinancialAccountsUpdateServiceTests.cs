using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Financials;

public class FinancialAccountsUpdateServiceTests
{
    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();

    private FinancialAccountsUpdateService Updater() => new(_db);

    private async Task SeedAsync(string? branchCode = "01")
    {
        _db.Context.FinancialAccounts.Add(new FinancialAccount
        {
            Code = "001", Name = "Banco", Type = FinancialAccountType.Bank, Currency = CurrencyType.Brl,
            BranchCode = branchCode
        });
        await _db.Context.SaveChangesAsync();
    }

    [Fact]
    public async Task Partial_update_clears_the_branch_and_keeps_other_fields()
    {
        await SeedAsync();

        await Updater().ExecuteAsync("001", a => a.BranchCode = null);

        var stored = await _db.Context.FinancialAccounts.AsNoTracking().SingleAsync();
        Assert.Null(stored.BranchCode);
        Assert.Equal("Banco", stored.Name);
    }

    [Fact]
    public async Task Partial_update_refuses_currency_change_when_account_has_settlements()
    {
        await SeedAsync();
        _db.Context.FinancialSettlements.Add(new FinancialSettlement
        {
            Key = Guid.NewGuid(), FinancialDocumentKey = Guid.NewGuid(), FinancialAccountCode = "001",
            SettlementDate = new DateTime(2026, 10, 1), Amount = 10m
        });
        await _db.Context.SaveChangesAsync();

        var e = await Assert.ThrowsAsync<ApplicationException>(
            () => Updater().ExecuteAsync("001", a => a.Currency = CurrencyType.Usd));
        Assert.Equal("A conta já possui baixas: não é possível alterar a moeda.", e.Message);
    }
}
