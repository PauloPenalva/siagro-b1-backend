using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Financials;

public class FinancialDocumentsSetDueDateServiceTests
{
    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();

    private FinancialDocumentsSetDueDateService Service() =>
        new(_db, new FinancialDocumentChangeLogService(_db.Context));

    private async Task<FinancialDocument> SeedAsync()
    {
        var document = new FinancialDocument
        {
            Key = Guid.NewGuid(),
            Code = "FN000001",
            CardCode = "F0001",
            Direction = FinancialDirection.Payable,
            Nature = FinancialDocumentNature.Provisional,
            DueDate = new DateTime(2026, 1, 31),
            NetAmount = 1000m
        };

        _db.Context.FinancialDocuments.Add(document);
        await _db.Context.SaveChangesAsync();
        return document;
    }

    [Fact]
    public async Task Changing_the_due_date_writes_one_log_line_with_both_values()
    {
        var document = await SeedAsync();

        await Service().ExecuteAsync(document.Key, new DateTime(2026, 3, 15), "tester");

        var log = Assert.Single(_db.Context.FinancialDocumentChangeLogs);
        Assert.Equal(FinancialDocumentChangeLogFields.DueDate, log.Field);
        Assert.Equal("31/01/2026", log.OldValue);
        Assert.Equal("15/03/2026", log.NewValue);
        Assert.Equal("tester", log.ChangedBy);
        Assert.Equal(new DateTime(2026, 3, 15), document.DueDate);
    }

    [Fact]
    public async Task Setting_the_same_date_writes_no_log_line()
    {
        var document = await SeedAsync();

        await Service().ExecuteAsync(document.Key, new DateTime(2026, 1, 31), "tester");

        Assert.Empty(_db.Context.FinancialDocumentChangeLogs);
    }

    [Fact]
    public async Task Refuses_to_correct_the_due_date_of_a_canceled_document()
    {
        var document = await SeedAsync();
        document.Status = FinancialDocumentStatus.Canceled;
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(document.Key, new DateTime(2026, 3, 15), "tester"));

        Assert.Contains(document.Code!, error.Message);
        Assert.Equal(new DateTime(2026, 1, 31), document.DueDate);
    }
}
