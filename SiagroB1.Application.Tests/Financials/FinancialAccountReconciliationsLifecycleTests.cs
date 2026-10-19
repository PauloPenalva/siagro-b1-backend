using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Financials;

public class FinancialAccountReconciliationsLifecycleTests
{
    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();

    private FinancialAccountReconciliationsOpenService Opener() => new(_db);
    private FinancialAccountReconciliationsSetLinesService Marker() => new(_db);
    private FinancialAccountReconciliationsCloseService Closer() => new(_db);
    private FinancialAccountReconciliationsReopenService Reopener() => new(_db);
    private FinancialAccountReconciliationsDeleteService Deleter() => new(_db);
    private FinancialAccountReconciliationsGetLinesService Lines() => new(_db);

    private async Task SeedAsync()
    {
        _db.Context.FinancialAccounts.AddRange(
            new FinancialAccount { Code = "BCO1", Name = "Banco 1", Type = FinancialAccountType.Bank, Currency = CurrencyType.Brl },
            new FinancialAccount { Code = "BCO2", Name = "Banco 2", Type = FinancialAccountType.Bank, Currency = CurrencyType.Brl });
        await _db.Context.SaveChangesAsync();
    }

    private async Task<FinancialAccountEntry> EntryAsync(decimal amount, DateTime date, string account = "BCO1")
    {
        var entry = new FinancialAccountEntry
        {
            Key = Guid.NewGuid(), FinancialAccountCode = account, EntryDate = date, Amount = amount, Description = "x"
        };
        _db.Context.FinancialAccountEntries.Add(entry);
        await _db.Context.SaveChangesAsync();
        return entry;
    }

    private async Task<FinancialSettlement> SettlementAsync(decimal amount, DateTime date)
    {
        var doc = new FinancialDocument
        {
            Key = Guid.NewGuid(), Code = "FN000001", CardCode = "C1", Direction = FinancialDirection.Receivable,
            Status = FinancialDocumentStatus.PartiallySettled, DueDate = date, NetAmount = 1000m, Currency = CurrencyType.Brl
        };
        var s = new FinancialSettlement
        {
            Key = Guid.NewGuid(), FinancialDocumentKey = doc.Key, FinancialAccountCode = "BCO1", SettlementDate = date,
            Amount = amount, Origin = FinancialSettlementOrigin.Manual
        };
        _db.Context.FinancialDocuments.Add(doc);
        _db.Context.FinancialSettlements.Add(s);
        await _db.Context.SaveChangesAsync();
        return s;
    }

    private static MovementRef Ref(FinancialAccountEntry e) => new(FinancialAccountMovementSource.Manual, e.Key);
    private static MovementRef Ref(FinancialSettlement s) => new(FinancialAccountMovementSource.Settlement, s.Key);
    private static readonly MovementRef[] None = [];

    private static readonly DateTime Oct31 = new(2026, 10, 31);

    [Fact]
    public async Task Marks_and_unmarks_lines_of_both_sources()
    {
        await SeedAsync();
        var entry = await EntryAsync(-10m, new DateTime(2026, 10, 5));
        var settlement = await SettlementAsync(300m, new DateTime(2026, 10, 6));
        var r = await Opener().ExecuteAsync("BCO1", Oct31, 290m, null, "t");

        await Marker().ExecuteAsync(r.Key, [Ref(entry), Ref(settlement)], None);
        Assert.Equal(r.Key, entry.ReconciliationKey);
        Assert.Equal(r.Key, settlement.ReconciliationKey);

        await Marker().ExecuteAsync(r.Key, None, [Ref(entry)]);
        Assert.Null(entry.ReconciliationKey);
    }

    [Fact]
    public async Task Refuses_line_of_another_account()
    {
        await SeedAsync();
        var entry = await EntryAsync(-10m, new DateTime(2026, 10, 5), "BCO2");
        var r = await Opener().ExecuteAsync("BCO1", Oct31, 0m, null, "t");

        var e = await Assert.ThrowsAsync<ApplicationException>(() => Marker().ExecuteAsync(r.Key, [Ref(entry)], None));
        Assert.Equal("Lançamento de outra conta.", e.Message);
    }

    [Fact]
    public async Task Refuses_line_after_statement_date()
    {
        await SeedAsync();
        var entry = await EntryAsync(-10m, new DateTime(2026, 11, 1));
        var r = await Opener().ExecuteAsync("BCO1", Oct31, 0m, null, "t");

        var e = await Assert.ThrowsAsync<ApplicationException>(() => Marker().ExecuteAsync(r.Key, [Ref(entry)], None));
        Assert.Equal("Lançamento posterior à data do extrato.", e.Message);
    }

    [Fact]
    public async Task Refuses_line_reconciled_elsewhere()
    {
        await SeedAsync();
        var entry = await EntryAsync(-10m, new DateTime(2026, 10, 5));
        entry.ReconciliationKey = Guid.NewGuid();
        await _db.Context.SaveChangesAsync();
        var r = await Opener().ExecuteAsync("BCO1", Oct31, 0m, null, "t");

        var e = await Assert.ThrowsAsync<ApplicationException>(() => Marker().ExecuteAsync(r.Key, [Ref(entry)], None));
        Assert.Equal("Lançamento já conciliado em outra reconciliação.", e.Message);
    }

    [Fact]
    public async Task Refuses_marking_a_closed_reconciliation()
    {
        await SeedAsync();
        var entry = await EntryAsync(-10m, new DateTime(2026, 10, 5));
        var r = await Opener().ExecuteAsync("BCO1", Oct31, 0m, null, "t");
        await Closer().ExecuteAsync(r.Key, "t");

        var e = await Assert.ThrowsAsync<ApplicationException>(() => Marker().ExecuteAsync(r.Key, [Ref(entry)], None));
        Assert.Equal("A reconciliação já está fechada.", e.Message);
    }

    [Fact]
    public async Task Close_refuses_when_there_is_a_difference()
    {
        await SeedAsync();
        var entry = await EntryAsync(-10m, new DateTime(2026, 10, 5));
        var r = await Opener().ExecuteAsync("BCO1", Oct31, -12m, null, "t");
        await Marker().ExecuteAsync(r.Key, [Ref(entry)], None);

        var e = await Assert.ThrowsAsync<ApplicationException>(() => Closer().ExecuteAsync(r.Key, "t"));
        Assert.Equal("Diferença de R$ 2,00 entre o saldo conciliado e o saldo do extrato.", e.Message);
    }

    [Fact]
    public async Task Close_succeeds_when_difference_is_zero()
    {
        await SeedAsync();
        var entry = await EntryAsync(-10m, new DateTime(2026, 10, 5));
        var settlement = await SettlementAsync(300m, new DateTime(2026, 10, 6));
        var r = await Opener().ExecuteAsync("BCO1", Oct31, 290m, null, "t");
        await Marker().ExecuteAsync(r.Key, [Ref(entry), Ref(settlement)], None);

        await Closer().ExecuteAsync(r.Key, "t");

        Assert.Equal(FinancialAccountReconciliationStatus.Closed, r.Status);
        Assert.NotNull(r.ClosedAt);
    }

    [Fact]
    public async Task Reopen_last_closed_keeps_marks()
    {
        await SeedAsync();
        var entry = await EntryAsync(-10m, new DateTime(2026, 10, 5));
        var r = await Opener().ExecuteAsync("BCO1", Oct31, -10m, null, "t");
        await Marker().ExecuteAsync(r.Key, [Ref(entry)], None);
        await Closer().ExecuteAsync(r.Key, "t");

        await Reopener().ExecuteAsync(r.Key, "t");

        Assert.Equal(FinancialAccountReconciliationStatus.Open, r.Status);
        Assert.Null(r.ClosedAt);
        Assert.Equal(r.Key, entry.ReconciliationKey);
    }

    [Fact]
    public async Task Refuses_to_reopen_a_reconciliation_that_is_not_the_last()
    {
        await SeedAsync();
        var first = await Opener().ExecuteAsync("BCO1", new DateTime(2026, 9, 30), 0m, null, "t");
        await Closer().ExecuteAsync(first.Key, "t");
        var second = await Opener().ExecuteAsync("BCO1", Oct31, 0m, null, "t");
        await Closer().ExecuteAsync(second.Key, "t");

        var e = await Assert.ThrowsAsync<ApplicationException>(() => Reopener().ExecuteAsync(first.Key, "t"));
        Assert.Equal("Somente a última reconciliação fechada pode ser reaberta.", e.Message);
    }

    [Fact]
    public async Task Refuses_to_reopen_while_another_is_open()
    {
        await SeedAsync();
        var first = await Opener().ExecuteAsync("BCO1", new DateTime(2026, 9, 30), 0m, null, "t");
        await Closer().ExecuteAsync(first.Key, "t");
        await Opener().ExecuteAsync("BCO1", Oct31, 0m, null, "t");

        var e = await Assert.ThrowsAsync<ApplicationException>(() => Reopener().ExecuteAsync(first.Key, "t"));
        Assert.Equal("Feche ou exclua a reconciliação aberta antes de reabrir outra.", e.Message);
    }

    [Fact]
    public async Task Delete_open_reconciliation_releases_its_lines()
    {
        await SeedAsync();
        var entry = await EntryAsync(-10m, new DateTime(2026, 10, 5));
        var settlement = await SettlementAsync(300m, new DateTime(2026, 10, 6));
        var r = await Opener().ExecuteAsync("BCO1", Oct31, 0m, null, "t");
        await Marker().ExecuteAsync(r.Key, [Ref(entry), Ref(settlement)], None);

        await Deleter().ExecuteAsync(r.Key);

        Assert.Null(entry.ReconciliationKey);
        Assert.Null(settlement.ReconciliationKey);
        Assert.False(await _db.Context.FinancialAccountReconciliations.AnyAsync());
    }

    [Fact]
    public async Task Refuses_to_delete_a_closed_reconciliation()
    {
        await SeedAsync();
        var r = await Opener().ExecuteAsync("BCO1", Oct31, 0m, null, "t");
        await Closer().ExecuteAsync(r.Key, "t");

        var e = await Assert.ThrowsAsync<ApplicationException>(() => Deleter().ExecuteAsync(r.Key));
        Assert.Equal("Reconciliação fechada não pode ser excluída. Reabra-a primeiro.", e.Message);
    }

    [Fact]
    public async Task GetLines_lists_pending_and_own_lines_up_to_statement_date_with_difference()
    {
        await SeedAsync();
        var mine = await EntryAsync(-10m, new DateTime(2026, 10, 5));
        await EntryAsync(-5m, new DateTime(2026, 10, 6));
        await EntryAsync(-7m, new DateTime(2026, 11, 2));
        var elsewhere = await EntryAsync(-3m, new DateTime(2026, 10, 7));
        elsewhere.ReconciliationKey = Guid.NewGuid();
        await _db.Context.SaveChangesAsync();
        var r = await Opener().ExecuteAsync("BCO1", Oct31, -15m, null, "t");
        await Marker().ExecuteAsync(r.Key, [Ref(mine)], None);

        var dto = await Lines().ExecuteAsync(r.Key);

        Assert.Equal(2, dto.Rows.Count);
        Assert.Single(dto.Rows, x => x.Marked);
        Assert.Equal(-10m, dto.ReconciledAmount);
        Assert.Equal(5m, dto.Difference); // conciliado (0 − 10) − extrato (−15)
    }

    [Fact]
    public async Task Reverse_reconciled_settlement_is_refused()
    {
        await SeedAsync();
        var settlement = await SettlementAsync(300m, new DateTime(2026, 10, 6));
        settlement.ReconciliationKey = Guid.NewGuid();
        await _db.Context.SaveChangesAsync();

        var e = await Assert.ThrowsAsync<ApplicationException>(
            () => new FinancialDocumentsReverseSettlementService(_db).ExecuteAsync(settlement.Key, "erro", "t"));
        Assert.Equal("Baixa conciliada não pode ser estornada. Remova a conciliação primeiro.", e.Message);
    }
}
