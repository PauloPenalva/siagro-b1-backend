using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Financials;

public class FinancialDocumentsRecalculateBalanceServiceTests
{
    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();

    private async Task<FinancialDocument> SeedDocumentAsync(decimal netAmount = 1000m)
    {
        var document = new FinancialDocument
        {
            Key = Guid.NewGuid(),
            Code = "FN000001",
            CardCode = "F0001",
            Direction = FinancialDirection.Payable,
            Nature = FinancialDocumentNature.Advance,
            DueDate = DateTime.Today.AddDays(30),
            NetAmount = netAmount
        };

        _db.Context.FinancialDocuments.Add(document);
        await _db.Context.SaveChangesAsync();
        return document;
    }

    private async Task AddSettlementAsync(Guid documentKey, decimal amount)
    {
        _db.Context.FinancialSettlements.Add(new FinancialSettlement
        {
            Key = Guid.NewGuid(),
            FinancialDocumentKey = documentKey,
            FinancialAccountCode = "CX01",
            SettlementDate = DateTime.Today,
            Amount = amount,
            Origin = amount >= 0
                ? FinancialSettlementOrigin.Manual
                : FinancialSettlementOrigin.Reversal
        });

        await _db.Context.SaveChangesAsync();
    }

    [Fact]
    public async Task An_untouched_document_is_open_with_the_full_amount_outstanding()
    {
        var document = await SeedDocumentAsync();

        await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(_db.Context, document.Key);
        await _db.Context.SaveChangesAsync();

        Assert.Equal(0m, document.SettledAmount);
        Assert.Equal(1000m, document.OpenAmount);
        Assert.Equal(FinancialDocumentStatus.Open, document.Status);
    }

    [Fact]
    public async Task A_partial_settlement_leaves_the_document_partially_settled()
    {
        var document = await SeedDocumentAsync();
        await AddSettlementAsync(document.Key, 400m);

        await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(_db.Context, document.Key);
        await _db.Context.SaveChangesAsync();

        Assert.Equal(400m, document.SettledAmount);
        Assert.Equal(600m, document.OpenAmount);
        Assert.Equal(FinancialDocumentStatus.PartiallySettled, document.Status);
    }

    [Fact]
    public async Task Settling_the_whole_amount_settles_the_document()
    {
        var document = await SeedDocumentAsync();
        await AddSettlementAsync(document.Key, 1000m);

        await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(_db.Context, document.Key);
        await _db.Context.SaveChangesAsync();

        Assert.Equal(FinancialDocumentStatus.Settled, document.Status);
        Assert.Equal(0m, document.OpenAmount);
    }

    [Fact]
    public async Task A_reversal_is_a_negative_line_and_reopens_the_document()
    {
        var document = await SeedDocumentAsync();
        await AddSettlementAsync(document.Key, 1000m);
        await AddSettlementAsync(document.Key, -1000m);

        await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(_db.Context, document.Key);
        await _db.Context.SaveChangesAsync();

        Assert.Equal(0m, document.SettledAmount);
        Assert.Equal(FinancialDocumentStatus.Open, document.Status);
    }

    [Fact]
    public async Task A_canceled_document_keeps_its_status_when_recalculated()
    {
        var document = await SeedDocumentAsync();
        document.Status = FinancialDocumentStatus.Canceled;
        await _db.Context.SaveChangesAsync();

        await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(_db.Context, document.Key);
        await _db.Context.SaveChangesAsync();

        Assert.Equal(FinancialDocumentStatus.Canceled, document.Status);
    }

    [Fact]
    public async Task A_document_worth_nothing_is_settled_not_open()
    {
        var document = await SeedDocumentAsync(netAmount: 0m);

        await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(_db.Context, document.Key);
        await _db.Context.SaveChangesAsync();

        Assert.Equal(FinancialDocumentStatus.Settled, document.Status);
        Assert.Equal(0m, document.OpenAmount);
    }

    [Fact]
    public async Task A_canceled_document_stays_canceled_even_when_fully_settled()
    {
        var document = await SeedDocumentAsync();
        await AddSettlementAsync(document.Key, 1000m);
        document.Status = FinancialDocumentStatus.Canceled;
        await _db.Context.SaveChangesAsync();

        await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(_db.Context, document.Key);
        await _db.Context.SaveChangesAsync();

        Assert.Equal(FinancialDocumentStatus.Canceled, document.Status);
        Assert.Equal(1000m, document.SettledAmount);   // o saldo AINDA e recalculado; so o status e preservado
    }
}
