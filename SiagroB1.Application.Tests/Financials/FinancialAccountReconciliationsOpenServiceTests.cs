using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Financials;

public class FinancialAccountReconciliationsOpenServiceTests
{
    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();

    private FinancialAccountReconciliationsOpenService Service() => new(_db);

    private async Task SeedAccountAsync()
    {
        _db.Context.FinancialAccounts.Add(new FinancialAccount
        {
            Code = "BCO1", Name = "Banco", Type = FinancialAccountType.Bank, Currency = CurrencyType.Brl
        });
        await _db.Context.SaveChangesAsync();
    }

    [Fact]
    public async Task Opens_with_zero_opening_balance_on_first_reconciliation()
    {
        await SeedAccountAsync();

        var r = await Service().ExecuteAsync("BCO1", new DateTime(2026, 10, 31, 10, 0, 0), 500m, null, "tester");

        Assert.Equal(FinancialAccountReconciliationStatus.Open, r.Status);
        Assert.Equal(new DateTime(2026, 10, 31), r.StatementDate);
        Assert.Equal(0m, r.OpeningBalance);
        Assert.Equal(500m, r.StatementBalance);
    }

    [Fact]
    public async Task Open_records_opening_balance_from_closed_reconciliations()
    {
        await SeedAccountAsync();
        var closed = new FinancialAccountReconciliation
        {
            Key = Guid.NewGuid(), FinancialAccountCode = "BCO1", StatementDate = new DateTime(2026, 9, 30),
            StatementBalance = 900m, Status = FinancialAccountReconciliationStatus.Closed
        };
        _db.Context.FinancialAccountReconciliations.Add(closed);
        _db.Context.FinancialAccountEntries.AddRange(
            new FinancialAccountEntry { FinancialAccountCode = "BCO1", EntryDate = new DateTime(2026, 9, 1), Amount = 1000m, Description = "Saldo", ReconciliationKey = closed.Key },
            new FinancialAccountEntry { FinancialAccountCode = "BCO1", EntryDate = new DateTime(2026, 9, 2), Amount = -100m, Description = "Tarifa", ReconciliationKey = closed.Key },
            new FinancialAccountEntry { FinancialAccountCode = "BCO1", EntryDate = new DateTime(2026, 9, 3), Amount = -40m, Description = "Pendente" });
        await _db.Context.SaveChangesAsync();

        var r = await Service().ExecuteAsync("BCO1", new DateTime(2026, 10, 31), 860m, null, "t");

        Assert.Equal(900m, r.OpeningBalance);
    }

    [Fact]
    public async Task Refuses_a_second_open_reconciliation()
    {
        await SeedAccountAsync();
        await Service().ExecuteAsync("BCO1", new DateTime(2026, 10, 31), 0m, null, "t");

        var e = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync("BCO1", new DateTime(2026, 11, 30), 0m, null, "t"));
        Assert.Equal("Já existe uma reconciliação aberta para esta conta.", e.Message);
    }

    [Fact]
    public async Task Refuses_statement_date_not_after_last_closed()
    {
        await SeedAccountAsync();
        _db.Context.FinancialAccountReconciliations.Add(new FinancialAccountReconciliation
        {
            FinancialAccountCode = "BCO1", StatementDate = new DateTime(2026, 9, 30), Status = FinancialAccountReconciliationStatus.Closed
        });
        await _db.Context.SaveChangesAsync();

        var e = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync("BCO1", new DateTime(2026, 9, 30), 0m, null, "t"));
        Assert.Equal("A data do extrato deve ser posterior à da última reconciliação fechada (30/09/2026).", e.Message);
    }

    [Fact]
    public async Task Refuses_unknown_account()
    {
        var e = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync("XX", new DateTime(2026, 9, 30), 0m, null, "t"));
        Assert.Equal("Conta financeira XX não encontrada.", e.Message);
    }

    [Fact]
    public async Task Double_click_race_on_the_unique_index_reports_the_open_reconciliation()
    {
        // Os dois cliques passam pelo AnyAsync; quem perde é barrado pelo índice único filtrado no SaveChanges.
        var name = Guid.NewGuid().ToString();
        var seed = TestDb.CreateUnitOfWork(name);
        seed.Context.FinancialAccounts.Add(new FinancialAccount
        {
            Code = "BCO1", Name = "Banco", Type = FinancialAccountType.Bank, Currency = CurrencyType.Brl
        });
        await seed.Context.SaveChangesAsync();
        var racing = TestDb.CreateUnitOfWork(name, new ThrowOnSaveInterceptor(
            e => e.Entity is FinancialAccountReconciliation && e.State == Microsoft.EntityFrameworkCore.EntityState.Added));

        var e = await Assert.ThrowsAsync<ApplicationException>(
            () => new FinancialAccountReconciliationsOpenService(racing).ExecuteAsync("BCO1", new DateTime(2026, 10, 31), 0m, null, "t"));
        Assert.Equal("Já existe uma reconciliação aberta para esta conta.", e.Message);
    }
}
