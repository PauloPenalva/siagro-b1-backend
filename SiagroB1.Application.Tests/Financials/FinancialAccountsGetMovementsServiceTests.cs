using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Financials;

public class FinancialAccountsGetMovementsServiceTests
{
    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();

    private FinancialAccountsGetMovementsService Service() => new(_db);

    private async Task SeedAsync()
    {
        _db.Context.FinancialAccounts.Add(new FinancialAccount
        {
            Code = "BCO1", Name = "Banco", Type = FinancialAccountType.Bank, Currency = CurrencyType.Brl
        });
        _db.Context.FinancialAccountEntries.AddRange(
            new FinancialAccountEntry { FinancialAccountCode = "BCO1", EntryDate = new DateTime(2026, 9, 1), Amount = 1000m, Description = "Saldo inicial" },
            new FinancialAccountEntry { FinancialAccountCode = "BCO1", EntryDate = new DateTime(2026, 10, 10), Amount = 200m, Description = "Depósito", CreatedAt = new DateTime(2026, 10, 10, 9, 0, 0) },
            new FinancialAccountEntry { FinancialAccountCode = "BCO1", EntryDate = new DateTime(2026, 10, 3), Amount = -50m, Description = "Tarifa" },
            new FinancialAccountEntry { FinancialAccountCode = "BCO1", EntryDate = new DateTime(2026, 10, 10), Amount = -30m, Description = "Tarifa 2", CreatedAt = new DateTime(2026, 10, 10, 10, 0, 0), ReconciliationKey = Guid.NewGuid() },
            new FinancialAccountEntry { FinancialAccountCode = "BCO1", EntryDate = new DateTime(2026, 11, 1), Amount = 999m, Description = "Fora do período" });
        await _db.Context.SaveChangesAsync();
    }

    [Fact]
    public async Task Running_balance_accumulates_from_the_opening_balance_in_date_order()
    {
        await SeedAsync();

        var dto = await Service().ExecuteAsync("BCO1", new DateTime(2026, 10, 1), new DateTime(2026, 10, 31));

        Assert.Equal(1000m, dto.OpeningBalance);
        Assert.Equal(new[] { "Tarifa", "Depósito", "Tarifa 2" }, dto.Rows.Select(r => r.Description));
        Assert.Equal(new[] { 950m, 1150m, 1120m }, dto.Rows.Select(r => r.RunningBalance));
        Assert.Equal(1120m, dto.ClosingBalance);
        Assert.Equal(200m, dto.TotalIn);
        Assert.Equal(80m, dto.TotalOut);
        Assert.True(dto.Rows[2].Reconciled);
    }

    [Fact]
    public async Task Empty_period_has_opening_equal_closing()
    {
        await SeedAsync();

        var dto = await Service().ExecuteAsync("BCO1", new DateTime(2026, 12, 1), new DateTime(2026, 12, 31));

        Assert.Empty(dto.Rows);
        Assert.Equal(dto.OpeningBalance, dto.ClosingBalance);
        Assert.Equal(2119m, dto.ClosingBalance);
    }

    [Fact]
    public async Task OnlyReconciled_keeps_only_reconciled_rows()
    {
        await SeedAsync();

        var dto = await Service().ExecuteAsync("BCO1", new DateTime(2026, 10, 1), new DateTime(2026, 10, 31), onlyReconciled: true);

        Assert.Equal("Tarifa 2", Assert.Single(dto.Rows).Description);
    }

    [Fact]
    public async Task Refuses_inverted_period()
    {
        await SeedAsync();
        var e = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync("BCO1", new DateTime(2026, 10, 31), new DateTime(2026, 10, 1)));
        Assert.Equal("A data inicial deve ser menor ou igual à final.", e.Message);
    }

    [Fact]
    public async Task Refuses_blank_account()
    {
        var e = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(" ", new DateTime(2026, 10, 1), new DateTime(2026, 10, 31)));
        Assert.Equal("Informe a conta financeira.", e.Message);
    }
}
