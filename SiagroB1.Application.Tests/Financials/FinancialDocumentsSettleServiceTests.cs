using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Financials;

public class FinancialDocumentsSettleServiceTests
{
    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();

    private FinancialDocumentsSettleService Service() => new(_db);
    private FinancialDocumentsReverseSettlementService Reverser() => new(_db);

    private async Task<FinancialDocument> SeedAsync(
        FinancialDocumentNature nature = FinancialDocumentNature.Advance,
        decimal netAmount = 1000m,
        CurrencyType currency = CurrencyType.Brl,
        FinancialDocumentStatus status = FinancialDocumentStatus.Open)
    {
        _db.Context.FinancialAccounts.Add(new FinancialAccount
        {
            Code = "CX01", Name = "Caixa", Type = FinancialAccountType.Cash, Currency = CurrencyType.Brl
        });

        var document = new FinancialDocument
        {
            Key = Guid.NewGuid(),
            Code = "FN000001",
            CardCode = "F0001",
            Direction = FinancialDirection.Payable,
            Nature = nature,
            Status = status,
            DueDate = DateTime.Today.AddDays(30),
            NetAmount = netAmount,
            Currency = currency
        };

        _db.Context.FinancialDocuments.Add(document);
        await _db.Context.SaveChangesAsync();
        return document;
    }

    [Fact]
    public async Task Settling_part_of_an_advance_leaves_it_partially_settled()
    {
        var document = await SeedAsync();

        await Service().ExecuteAsync(document.Key, "CX01", 400m, DateTime.Today,
            0m, 0m, 0m, "OP-1", null, "tester");

        Assert.Equal(400m, document.SettledAmount);
        Assert.Equal(FinancialDocumentStatus.PartiallySettled, document.Status);
        Assert.Equal(400m, document.AvailableAdvanceAmount);
    }

    [Fact]
    public async Task Refuses_to_settle_a_provisional_document()
    {
        var document = await SeedAsync(nature: FinancialDocumentNature.Provisional);

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(document.Key, "CX01", 100m, DateTime.Today,
                0m, 0m, 0m, null, null, "tester"));

        Assert.Contains("provisório", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refuses_to_settle_more_than_the_outstanding_amount()
    {
        var document = await SeedAsync(netAmount: 1000m);

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(document.Key, "CX01", 1500m, DateTime.Today,
                0m, 0m, 0m, null, null, "tester"));

        Assert.Contains("saldo", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refuses_an_account_in_a_different_currency()
    {
        var document = await SeedAsync(currency: CurrencyType.Usd);

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(document.Key, "CX01", 100m, DateTime.Today,
                0m, 0m, 0m, null, null, "tester"));

        Assert.Contains("moeda", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refuses_to_settle_a_canceled_document()
    {
        var document = await SeedAsync(status: FinancialDocumentStatus.Canceled);

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(document.Key, "CX01", 100m, DateTime.Today,
                0m, 0m, 0m, null, null, "tester"));

        Assert.Contains("cancelado", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Interest_and_fine_do_not_change_what_the_document_owes()
    {
        var document = await SeedAsync(netAmount: 1000m);

        await Service().ExecuteAsync(document.Key, "CX01", 1000m, DateTime.Today,
            interest: 50m, fine: 20m, discount: 0m, "OP-2", null, "tester");

        Assert.Equal(1000m, document.SettledAmount);
        Assert.Equal(FinancialDocumentStatus.Settled, document.Status);
    }

    [Fact]
    public async Task Reversing_a_settlement_writes_a_negative_line_and_reopens_the_document()
    {
        var document = await SeedAsync();
        var settlement = await Service().ExecuteAsync(document.Key, "CX01", 1000m, DateTime.Today,
            0m, 0m, 0m, null, null, "tester");

        await Reverser().ExecuteAsync(settlement.Key, "Pagamento indevido", "tester");

        Assert.Equal(0m, document.SettledAmount);
        Assert.Equal(FinancialDocumentStatus.Open, document.Status);
        Assert.Equal(2, _db.Context.FinancialSettlements.Count());
    }

    [Fact]
    public async Task The_same_settlement_cannot_be_reversed_twice()
    {
        var document = await SeedAsync();
        var settlement = await Service().ExecuteAsync(document.Key, "CX01", 1000m, DateTime.Today,
            0m, 0m, 0m, null, null, "tester");

        await Reverser().ExecuteAsync(settlement.Key, "Pagamento indevido", "tester");

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Reverser().ExecuteAsync(settlement.Key, "De novo", "tester"));

        Assert.Contains("estornada", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
