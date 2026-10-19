using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Financials;

public class FinancialDocumentsManualServicesTests
{
    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();
    private readonly FakeDocNumberSequenceService _docNumbers = new();

    private FinancialDocumentsManualCreateService CreateService() => new(
        _db, _docNumbers,
        new FakeBusinessPartnerService(new Dictionary<string, string> { ["C0001"] = "CLIENTE TESTE", ["C0002"] = "OUTRO" }));

    private FinancialDocumentsManualUpdateService UpdateService() => new(
        _db,
        new FakeBusinessPartnerService(new Dictionary<string, string> { ["C0001"] = "CLIENTE TESTE", ["C0002"] = "OUTRO" }),
        new FinancialDocumentChangeLogService(_db.Context));

    private FinancialDocumentsManualDeleteService DeleteService() => new(_db);

    private static FinancialDocument NewInput(FinancialDirection direction = FinancialDirection.Receivable) => new()
    {
        CardCode = "C0001",
        BranchCode = "01",
        Direction = direction,
        DocumentDate = DateTime.Today,
        DueDate = DateTime.Today.AddDays(30),
        NetAmount = 1500.456m,
        Comments = "Aluguel",
    };

    private async Task<FinancialDocument> CreateAsync(FinancialDocument? input = null) =>
        await CreateService().ExecuteAsync(input ?? NewInput(), "tester");

    [Fact]
    public async Task Creates_a_manual_firm_title_with_automatic_number()
    {
        var doc = await CreateAsync();

        Assert.Equal(FinancialDocumentOrigin.Manual, doc.OriginType);
        Assert.Equal(FinancialDocumentNature.Firm, doc.Nature);
        Assert.Equal(FinancialDocumentStatus.Open, doc.Status);
        Assert.Equal(FinancialDirection.Receivable, doc.Direction);
        Assert.Equal(1500.46m, doc.NetAmount);
        Assert.Equal("CLIENTE TESTE", doc.CardName);
        Assert.False(doc.IsBlockedForSettlement);
        Assert.StartsWith("ST-", doc.Code);
        Assert.Equal(TransactionCode.FinancialDocument, _docNumbers.Requests.Single().TransactionCode);
        Assert.Equal(1, _db.Context.FinancialDocuments.Count());
    }

    [Fact]
    public async Task Manual_series_uses_the_typed_code()
    {
        var input = NewInput();
        input.Code = "  NF-123 ";
        input.DocNumberKey = Guid.NewGuid();

        var doc = await CreateAsync(input);

        Assert.Equal("NF-123", doc.Code);
        Assert.Equal(input.DocNumberKey, doc.DocNumberKey);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Refuses_a_non_positive_amount(decimal amount)
    {
        var input = NewInput();
        input.NetAmount = amount;

        var error = await Assert.ThrowsAsync<ApplicationException>(() => CreateAsync(input));
        Assert.Contains("valor", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refuses_an_unknown_partner()
    {
        var input = NewInput();
        input.CardCode = "X9999";

        await Assert.ThrowsAsync<ApplicationException>(() => CreateAsync(input));
    }

    [Fact]
    public async Task Refuses_due_date_before_document_date()
    {
        var input = NewInput();
        input.DueDate = DateTime.Today.AddDays(-1);

        var error = await Assert.ThrowsAsync<ApplicationException>(() => CreateAsync(input));
        Assert.Contains("vencimento", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refuses_missing_branch()
    {
        var input = NewInput();
        input.BranchCode = " ";

        await Assert.ThrowsAsync<ApplicationException>(() => CreateAsync(input));
    }

    [Fact]
    public async Task Updates_open_title_and_logs_changes()
    {
        var doc = await CreateAsync();

        await UpdateService().ExecuteAsync(doc.Key, new ManualFinancialDocumentChanges
        {
            CardCode = "C0002",
            NetAmount = 2000m,
            DueDate = DateTime.Today.AddDays(60),
            HasComments = true, Comments = "Novo",
        }, "tester");

        var saved = _db.Context.FinancialDocuments.Single();
        Assert.Equal("C0002", saved.CardCode);
        Assert.Equal("OUTRO", saved.CardName);
        Assert.Equal(2000m, saved.NetAmount);
        Assert.Equal("Novo", saved.Comments);
        var fields = _db.Context.FinancialDocumentChangeLogs.Select(x => x.Field).ToList();
        Assert.Contains(FinancialDocumentChangeLogFields.NetAmount, fields);
        Assert.Contains(FinancialDocumentChangeLogFields.DueDate, fields);
        Assert.Contains(FinancialDocumentChangeLogFields.Partner, fields);
        Assert.Contains(FinancialDocumentChangeLogFields.Comments, fields);
    }

    [Fact]
    public async Task Settled_title_only_changes_due_date_and_texts()
    {
        var doc = await CreateAsync();
        await SettleAsync(doc, 100m);

        await UpdateService().ExecuteAsync(doc.Key, new ManualFinancialDocumentChanges
        {
            DueDate = DateTime.Today.AddDays(90),
            HasComments = true, Comments = "ok",
        }, "tester");

        var error = await Assert.ThrowsAsync<ApplicationException>(() =>
            UpdateService().ExecuteAsync(doc.Key, new ManualFinancialDocumentChanges { NetAmount = 50m }, "tester"));
        Assert.Contains("baixa", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refuses_editing_a_generated_title()
    {
        var doc = await SeedGeneratedAsync();

        await Assert.ThrowsAsync<ApplicationException>(() =>
            UpdateService().ExecuteAsync(doc.Key, new ManualFinancialDocumentChanges { HasComments = true, Comments = "x" }, "tester"));
    }

    [Fact]
    public async Task Refuses_editing_a_canceled_title()
    {
        var doc = await CreateAsync();
        doc.Status = FinancialDocumentStatus.Canceled;
        await _db.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<ApplicationException>(() =>
            UpdateService().ExecuteAsync(doc.Key, new ManualFinancialDocumentChanges { HasComments = true, Comments = "x" }, "tester"));
    }

    [Fact]
    public async Task Deletes_a_manual_title_without_settlements_and_its_log()
    {
        var doc = await CreateAsync();
        await UpdateService().ExecuteAsync(doc.Key, new ManualFinancialDocumentChanges { HasComments = true, Comments = "x" }, "tester");

        await DeleteService().ExecuteAsync(doc.Key);

        Assert.Empty(_db.Context.FinancialDocuments);
        Assert.Empty(_db.Context.FinancialDocumentChangeLogs);
    }

    [Fact]
    public async Task Refuses_deleting_a_title_with_settlement_history()
    {
        var doc = await CreateAsync();
        await SettleAsync(doc, 0m);

        var error = await Assert.ThrowsAsync<ApplicationException>(() => DeleteService().ExecuteAsync(doc.Key));
        Assert.Contains("baixa", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refuses_deleting_generated_title_or_advance()
    {
        var generated = await SeedGeneratedAsync();
        var advance = await CreateAsync();
        advance.Nature = FinancialDocumentNature.Advance;
        await _db.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<ApplicationException>(() => DeleteService().ExecuteAsync(generated.Key));
        await Assert.ThrowsAsync<ApplicationException>(() => DeleteService().ExecuteAsync(advance.Key));
    }

    [Fact]
    public async Task Delete_of_unknown_title_is_not_found()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => DeleteService().ExecuteAsync(Guid.NewGuid()));
    }

    private async Task SettleAsync(FinancialDocument doc, decimal amount)
    {
        _db.Context.FinancialSettlements.Add(new FinancialSettlement
        {
            Key = Guid.NewGuid(),
            FinancialDocumentKey = doc.Key,
            Amount = amount,
        });
        doc.SettledAmount = amount;
        await _db.Context.SaveChangesAsync();
    }

    private async Task<FinancialDocument> SeedGeneratedAsync()
    {
        var doc = new FinancialDocument
        {
            Key = Guid.NewGuid(),
            Code = "G1",
            CardCode = "C0001",
            BranchCode = "01",
            Direction = FinancialDirection.Receivable,
            Nature = FinancialDocumentNature.Firm,
            OriginType = FinancialDocumentOrigin.SalesInvoiceNoContract,
            DueDate = DateTime.Today,
            NetAmount = 10m,
        };
        _db.Context.FinancialDocuments.Add(doc);
        await _db.Context.SaveChangesAsync();
        return doc;
    }
}
