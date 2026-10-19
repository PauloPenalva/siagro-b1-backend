using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Infra.Queries;

namespace SiagroB1.Application.Tests.Financials;

public class FinancialAccountMovementsQueryTests
{
    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();

    private async Task<FinancialDocument> SeedDocumentAsync(FinancialDirection direction)
    {
        if (!await _db.Context.FinancialAccounts.AnyAsync())
        {
            _db.Context.FinancialAccounts.AddRange(
                new FinancialAccount { Code = "BCO1", Name = "Banco 1", Type = FinancialAccountType.Bank, Currency = CurrencyType.Brl },
                new FinancialAccount { Code = "BCO2", Name = "Banco 2", Type = FinancialAccountType.Bank, Currency = CurrencyType.Brl });
        }

        var document = new FinancialDocument
        {
            Key = Guid.NewGuid(), Code = "FN000001", CardCode = "C0001", CardName = "Cliente",
            Direction = direction, Status = FinancialDocumentStatus.Open,
            DueDate = DateTime.Today, NetAmount = 1000m, Currency = CurrencyType.Brl
        };
        _db.Context.FinancialDocuments.Add(document);
        await _db.Context.SaveChangesAsync();
        return document;
    }

    private FinancialSettlement Settlement(FinancialDocument doc, string? account = "BCO1", DateTime? date = null) => new()
    {
        Key = Guid.NewGuid(), FinancialDocumentKey = doc.Key, FinancialAccountCode = account,
        SettlementDate = date ?? new DateTime(2026, 10, 10), Amount = 100m, InterestAmount = 5m, FineAmount = 2m,
        DiscountAmount = 3m, Origin = FinancialSettlementOrigin.Manual
    };

    private Task<List<FinancialAccountMovementRow>> RowsAsync(string account = "BCO1") =>
        FinancialAccountMovementsQuery.Query(_db.Context, account).ToListAsync();

    [Fact]
    public async Task Receivable_settlement_is_cash_in_with_interest_fine_minus_discount()
    {
        var doc = await SeedDocumentAsync(FinancialDirection.Receivable);
        _db.Context.FinancialSettlements.Add(Settlement(doc));
        await _db.Context.SaveChangesAsync();

        var row = Assert.Single(await RowsAsync());
        Assert.Equal(104m, row.Amount);
        Assert.Equal(FinancialAccountMovementSource.Settlement, row.Source);
        Assert.Equal(doc.Key, row.FinancialDocumentKey);
        Assert.Equal("C0001", row.CardCode);
    }

    [Fact]
    public async Task Payable_settlement_is_cash_out()
    {
        var doc = await SeedDocumentAsync(FinancialDirection.Payable);
        _db.Context.FinancialSettlements.Add(Settlement(doc));
        await _db.Context.SaveChangesAsync();

        Assert.Equal(-104m, Assert.Single(await RowsAsync()).Amount);
    }

    [Fact]
    public async Task Settlement_and_its_reversal_sum_to_zero()
    {
        var doc = await SeedDocumentAsync(FinancialDirection.Receivable);
        var original = Settlement(doc);
        _db.Context.FinancialSettlements.Add(original);
        _db.Context.FinancialSettlements.Add(new FinancialSettlement
        {
            Key = Guid.NewGuid(), FinancialDocumentKey = doc.Key, FinancialAccountCode = "BCO1",
            SettlementDate = original.SettlementDate, Amount = -100m, InterestAmount = -5m, FineAmount = -2m,
            DiscountAmount = -3m, Origin = FinancialSettlementOrigin.Reversal, ReversedSettlementKey = original.Key
        });
        await _db.Context.SaveChangesAsync();

        var rows = await RowsAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(0m, rows.Sum(r => r.Amount));
    }

    [Fact]
    public async Task Settlement_without_account_is_excluded()
    {
        var doc = await SeedDocumentAsync(FinancialDirection.Receivable);
        _db.Context.FinancialSettlements.Add(Settlement(doc, account: null));
        await _db.Context.SaveChangesAsync();

        Assert.Empty(await RowsAsync());
    }

    [Fact]
    public async Task Manual_entries_of_other_accounts_are_excluded()
    {
        await SeedDocumentAsync(FinancialDirection.Receivable);
        _db.Context.FinancialAccountEntries.AddRange(
            new FinancialAccountEntry { FinancialAccountCode = "BCO1", EntryDate = new DateTime(2026, 10, 1), Amount = -12.5m, Description = "Tarifa" },
            new FinancialAccountEntry { FinancialAccountCode = "BCO2", EntryDate = new DateTime(2026, 10, 1), Amount = 50m, Description = "Outra" });
        await _db.Context.SaveChangesAsync();

        var row = Assert.Single(await RowsAsync());
        Assert.Equal(-12.5m, row.Amount);
        Assert.Equal(FinancialAccountMovementSource.Manual, row.Source);
        Assert.Equal("Tarifa", row.Description);
    }

    [Fact]
    public async Task BalanceBefore_sums_only_rows_before_the_date()
    {
        var doc = await SeedDocumentAsync(FinancialDirection.Receivable);
        _db.Context.FinancialSettlements.Add(Settlement(doc, date: new DateTime(2026, 9, 30, 15, 0, 0)));
        _db.Context.FinancialSettlements.Add(Settlement(doc, date: new DateTime(2026, 10, 1)));
        _db.Context.FinancialAccountEntries.Add(new FinancialAccountEntry
        {
            FinancialAccountCode = "BCO1", EntryDate = new DateTime(2026, 9, 1), Amount = 1000m, Description = "Saldo inicial"
        });
        await _db.Context.SaveChangesAsync();

        var balance = await FinancialAccountMovementsQuery.BalanceBeforeAsync(
            _db.Context, "BCO1", new DateTime(2026, 10, 1, 18, 0, 0), CancellationToken.None);

        Assert.Equal(1104m, balance);
    }

    [Fact]
    public void Query_translates_to_sql_server()
    {
        // InMemory aceita LINQ que o SQL Server recusa; ToQueryString traduz sem conectar.
        var options = new DbContextOptionsBuilder<SiagroB1.Infra.Context.AppDbContext>()
            .UseSqlServer("Server=.;Database=x;Trusted_Connection=True;TrustServerCertificate=True").Options;
        using var ctx = new SiagroB1.Infra.Context.AppDbContext(options);

        var sql = FinancialAccountMovementsQuery.Query(ctx, "BCO1")
            .Where(r => r.Date < new DateTime(2026, 10, 1))
            .OrderBy(r => r.Date).ThenBy(r => r.CreatedAt)
            .ToQueryString();

        Assert.Contains("UNION ALL", sql);
    }
}
