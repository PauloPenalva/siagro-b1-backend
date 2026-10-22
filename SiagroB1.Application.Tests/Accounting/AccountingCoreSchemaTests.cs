using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.Accounting;

/// <summary>Esquema do núcleo contábil (CT2/CTG/configurações).</summary>
public class AccountingCoreSchemaTests
{
    [Fact]
    public void Model_has_unique_document_index_and_source_index()
    {
        var model = TestDb.CreateUnitOfWork().Context.Model;
        var entry = model.FindEntityType(typeof(JournalEntry))!;

        var doc = entry.GetIndexes().Single(i => i.Properties.Select(p => p.Name).SequenceEqual(
            [nameof(JournalEntry.PostingDate), nameof(JournalEntry.Batch), nameof(JournalEntry.SubBatch), nameof(JournalEntry.DocumentNumber)]));
        Assert.True(doc.IsUnique);

        var source = entry.GetIndexes().Single(i => i.Properties.Select(p => p.Name).SequenceEqual(
        [
            nameof(JournalEntry.StandardPostingCode), nameof(JournalEntry.SourceDocumentType),
            nameof(JournalEntry.SourceDocumentKey), nameof(JournalEntry.SourceEvent),
        ]));
        Assert.True(source.IsUnique);
        Assert.Equal("[SourceDocumentKey] IS NOT NULL AND [ReversalOfDocEntry] IS NULL", source.GetFilter());

        var period = model.FindEntityType(typeof(AccountingPeriod))!;
        Assert.Contains(period.GetIndexes(), i => i.IsUnique &&
            i.Properties.Select(p => p.Name).SequenceEqual([nameof(AccountingPeriod.Year), nameof(AccountingPeriod.Month)]));

        // O provider InMemory não aplica o tipo relacional; confere o atributo que o SQL Server usa.
        var column = typeof(JournalEntryLine).GetProperty(nameof(JournalEntryLine.Amount))!
            .GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.Schema.ColumnAttribute), false)
            .Cast<System.ComponentModel.DataAnnotations.Schema.ColumnAttribute>().Single();
        Assert.Equal("decimal(19,2)", column.TypeName);
    }

    [Fact]
    public async Task Persists_entry_with_lines_and_settings()
    {
        var name = Guid.NewGuid().ToString();
        var db = TestDb.CreateUnitOfWork(name);
        db.Context.JournalEntries.Add(new JournalEntry
        {
            PostingDate = new DateTime(2026, 10, 22), BranchCode = "01", Batch = "008890", DocumentNumber = "000001",
            Origin = JournalEntryOrigin.Manual, CreatedBy = "admin",
            Lines =
            [
                new JournalEntryLine
                {
                    LineNumber = 1, EntryType = JournalEntryLineType.Double, DebitAccountCode = "1.1.01",
                    CreditAccountCode = "3.1.01", Amount = 100m, History = "Teste",
                },
            ],
        });
        db.Context.AccountingSettings.Add(new AccountingSettings { CutoverDate = new DateTime(2026, 1, 1), UpdatedBy = "admin" });
        db.Context.AccountingPeriods.Add(new AccountingPeriod
        {
            Year = 2026, Month = 10, StartDate = new DateTime(2026, 10, 1), EndDate = new DateTime(2026, 10, 31),
        });
        await db.SaveChangesAsync();

        var read = TestDb.CreateUnitOfWork(name).Context;
        var e = await read.JournalEntries.Include(x => x.Lines).SingleAsync();
        Assert.Equal("001", e.SubBatch);
        Assert.Equal(100m, e.Lines.Single().Amount);
        Assert.Equal(AccountingClosingFrequency.Monthly, (await read.AccountingSettings.SingleAsync()).ClosingFrequency);
        Assert.Equal(AccountingPeriodStatus.Open, (await read.AccountingPeriods.SingleAsync()).Status);
    }
}
