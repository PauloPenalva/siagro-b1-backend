using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>
/// Devolução do adiantamento: linha NEGATIVA com origem própria e o título cancelado.
/// A origem separada de Reversal é o ponto — o estorno diz que a baixa não deveria ter
/// existido, a devolução diz que ela existiu e o dinheiro voltou.
/// </summary>
public class FinancialAdvancesRefundServiceTests
{
    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();

    private FinancialAdvancesRefundService Service() => new(_db);
    private FinancialDocumentsSettleService Settler() => new(_db);
    private FinancialDocumentsReverseSettlementService Reverser() => new(_db);

    private async Task<FinancialDocument> SeedAsync(
        FinancialDocumentNature nature = FinancialDocumentNature.Advance,
        decimal netAmount = 1000m,
        decimal settle = 1000m,
        FinancialDocumentStatus status = FinancialDocumentStatus.Open)
    {
        _db.Context.FinancialAccounts.Add(new FinancialAccount
        {
            Code = "CX01", Name = "Caixa", Type = FinancialAccountType.Cash, Currency = CurrencyType.Brl
        });

        var document = new FinancialDocument
        {
            Key = Guid.NewGuid(),
            Code = "FN000217",
            CardCode = "F0001",
            Direction = FinancialDirection.Payable,
            Nature = nature,
            Status = status,
            DueDate = DateTime.Today.AddDays(30),
            NetAmount = netAmount,
            Currency = CurrencyType.Brl
        };

        _db.Context.FinancialDocuments.Add(document);
        await _db.Context.SaveChangesAsync();

        if (settle > 0m && nature == FinancialDocumentNature.Advance)
            await Settler().ExecuteAsync(document.Key, "CX01", settle, DateTime.Today,
                0m, 0m, 0m, "OP-1", null, "tester");

        return document;
    }

    [Fact]
    public async Task Refunding_writes_a_negative_line_zeroes_the_balance_and_cancels_the_document()
    {
        var document = await SeedAsync();

        await Service().ExecuteAsync(document.Key, "CX01", new DateTime(2026, 9, 8),
            "TED-99", "produtor desistiu", "tester");

        var refund = _db.Context.FinancialSettlements
            .Single(x => x.Origin == FinancialSettlementOrigin.AdvanceRefund);

        Assert.Equal(-1000m, refund.Amount);
        Assert.Equal(0m, refund.InterestAmount);
        Assert.Equal(0m, refund.FineAmount);
        Assert.Equal(0m, refund.DiscountAmount);
        Assert.Equal("CX01", refund.FinancialAccountCode);
        Assert.Equal(new DateTime(2026, 9, 8), refund.SettlementDate);
        Assert.Equal("TED-99", refund.DocumentReference);
        Assert.Null(refund.ReversedSettlementKey);
        Assert.Equal("produtor desistiu", refund.Notes);

        Assert.Equal(0m, document.SettledAmount);
        Assert.Equal(0m, document.AvailableAdvanceAmount);
        Assert.Equal(FinancialDocumentStatus.Canceled, document.Status);
        Assert.Contains("produtor desistiu", document.CancellationReason);
        Assert.Equal("tester", document.CanceledBy);
    }

    [Fact]
    public async Task Refusing_a_second_refund_of_the_same_advance()
    {
        var document = await SeedAsync();

        await Service().ExecuteAsync(document.Key, "CX01", DateTime.Today, null, "primeira", "tester");

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(document.Key, "CX01", DateTime.Today, null, "segunda", "tester"));

        Assert.Contains("cancelado", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refuses_an_advance_with_nothing_settled()
    {
        var document = await SeedAsync(settle: 0m);

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(document.Key, "CX01", DateTime.Today, null, "motivo", "tester"));

        Assert.Contains("não há", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refuses_a_document_that_is_not_an_advance()
    {
        var document = await SeedAsync(nature: FinancialDocumentNature.Provisional, settle: 0m);

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(document.Key, "CX01", DateTime.Today, null, "motivo", "tester"));

        Assert.Contains("adiantamento", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refuses_a_refund_without_a_reason()
    {
        var document = await SeedAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(document.Key, "CX01", DateTime.Today, null, "  ", "tester"));

        Assert.Contains("motivo", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refuses_an_unknown_financial_account()
    {
        var document = await SeedAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(document.Key, "NAOEXISTE", DateTime.Today, null, "motivo", "tester"));

        Assert.Contains("conta financeira", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refuses_an_inactive_financial_account()
    {
        var document = await SeedAsync();

        _db.Context.FinancialAccounts.Add(new FinancialAccount
        {
            Code = "CX02", Name = "Caixa velho", Type = FinancialAccountType.Cash,
            Currency = CurrencyType.Brl, Inactive = true
        });
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(document.Key, "CX02", DateTime.Today, null, "motivo", "tester"));

        Assert.Contains("inativa", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refuses_a_document_that_does_not_exist()
    {
        await SeedAsync();

        await Assert.ThrowsAsync<SiagroB1.Domain.Exceptions.NotFoundException>(
            () => Service().ExecuteAsync(Guid.NewGuid(), "CX01", DateTime.Today, null, "motivo", "tester"));
    }

    [Fact]
    public async Task Refuses_a_refund_into_an_account_of_a_different_currency()
    {
        var document = await SeedAsync();

        _db.Context.FinancialAccounts.Add(new FinancialAccount
        {
            Code = "USD01", Name = "Conta dólar", Type = FinancialAccountType.Cash,
            Currency = CurrencyType.Usd
        });
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(document.Key, "USD01", DateTime.Today, null, "motivo", "tester"));

        Assert.Contains("moeda", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// C1 — a devolução não pode ser desfeita por "Estornar baixa": ela É o registro de que o
    /// dinheiro voltou. Sem este guard, estornar esta linha reabria o saldo com o título ainda
    /// Canceled, reabrindo o buraco que esta feature existe para fechar.
    /// </summary>
    [Fact]
    public async Task Reversing_the_refund_row_itself_is_refused_and_leaves_the_ledger_untouched()
    {
        var document = await SeedAsync();

        await Service().ExecuteAsync(document.Key, "CX01", DateTime.Today, null, "produtor desistiu", "tester");

        var refund = _db.Context.FinancialSettlements
            .Single(x => x.Origin == FinancialSettlementOrigin.AdvanceRefund);

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Reverser().ExecuteAsync(refund.Key, "tentando reabrir", "tester"));

        Assert.Contains("devolução", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, _db.Context.FinancialSettlements.Count());
    }

    /// <summary>
    /// C1 — mirror: estornar a baixa ORIGINAL depois de uma devolução também precisa ser barrado.
    /// Aqui quem pega é o guard de Status == Canceled, não o de Origin — a linha original é
    /// Manual, não AdvanceRefund.
    /// </summary>
    [Fact]
    public async Task Reversing_the_original_settlement_after_a_refund_is_refused_by_the_canceled_guard()
    {
        var document = await SeedAsync();

        var original = _db.Context.FinancialSettlements
            .Single(x => x.Origin == FinancialSettlementOrigin.Manual);

        await Service().ExecuteAsync(document.Key, "CX01", DateTime.Today, null, "produtor desistiu", "tester");

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Reverser().ExecuteAsync(original.Key, "tentando reabrir", "tester"));

        Assert.Contains("cancelado", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, _db.Context.FinancialSettlements.Count());
    }
}
