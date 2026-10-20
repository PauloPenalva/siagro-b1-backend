using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>Spec 2026-10-14, regras 1–4: compensar o crédito do adiantamento contra o provisório, em par.</summary>
public class FinancialDocumentsApplyAdvanceTests
{
    [Fact]
    public async Task Applying_writes_the_pair_recalculates_both_and_logs_both()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();

        await seed.ApplyAsync(6_000m);

        var onProvisional = await seed.LineAsync(seed.Provisional.Key, FinancialSettlementOrigin.AdvanceApplication);
        Assert.Equal(6_000m, onProvisional.Amount);
        Assert.Null(onProvisional.FinancialAccountCode);
        Assert.Equal(seed.Advance.Key, onProvisional.RelatedDocumentKey);
        Assert.Equal("FN-ADT-1", onProvisional.DocumentReference);
        Assert.Equal(new DateTime(2026, 10, 14), onProvisional.SettlementDate);

        var onAdvance = await seed.LineAsync(seed.Advance.Key, FinancialSettlementOrigin.AdvanceApplication);
        Assert.Equal(6_000m, onAdvance.Amount);
        Assert.Null(onAdvance.FinancialAccountCode);
        Assert.Equal(seed.Provisional.Key, onAdvance.RelatedDocumentKey);
        Assert.Equal("FN-PROV-1", onAdvance.DocumentReference);

        var provisional = await seed.ReloadAsync(seed.Provisional.Key);
        Assert.Equal(6_000m, provisional.SettledAmount);
        Assert.Equal(13_728.33m, provisional.AvailableToSettleAmount);
        Assert.Equal(FinancialDocumentStatus.PartiallySettled, provisional.Status);

        var advance = await seed.ReloadAsync(seed.Advance.Key);
        Assert.Equal(10_000m, advance.SettledAmount);
        Assert.Equal(6_000m, advance.AppliedAmount);
        Assert.Equal(4_000m, advance.AvailableAdvanceAmount);
        Assert.Equal(FinancialDocumentStatus.Settled, advance.Status);

        var logs = await seed.Context.FinancialDocumentChangeLogs.AsNoTracking()
            .Where(l => l.Field == FinancialDocumentChangeLogFields.AdvanceApplication)
            .ToListAsync();
        Assert.Equal(2, logs.Count);
        Assert.Equal("FN-ADT-1: 6.000,00", logs.Single(l => l.FinancialDocumentKey == seed.Provisional.Key).NewValue);
        Assert.Equal("FN-PROV-1: 6.000,00", logs.Single(l => l.FinancialDocumentKey == seed.Advance.Key).NewValue);
        Assert.All(logs, l => Assert.Null(l.OldValue));
    }

    [Fact]
    public async Task Application_can_settle_the_provisional()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync(
            provisionalAmount: FirmPayableSeed.DefaultFirmAmount, advanceAmount: 20_000m, advancePaid: 20_000m);

        await seed.ApplyAsync(FirmPayableSeed.DefaultFirmAmount);

        var provisional = await seed.ReloadAsync(seed.Provisional.Key);
        Assert.Equal(FinancialDocumentStatus.Settled, provisional.Status);
        Assert.Equal(0m, provisional.AvailableToSettleAmount);

        var advance = await seed.ReloadAsync(seed.Advance.Key);
        Assert.Equal(19_728.33m, advance.AppliedAmount);
        Assert.Equal(271.67m, advance.AvailableAdvanceAmount);
    }

    [Fact]
    public async Task Above_the_provisional_available_is_refused()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync(advanceAmount: 30_000m, advancePaid: 30_000m);

        var error = await Assert.ThrowsAsync<ApplicationException>(() => seed.ApplyAsync(20_000m));

        Assert.Equal("Valor acima do disponível para baixa do título FN-PROV-1 (19.728,33).", error.Message);
        Assert.Empty(seed.Context.FinancialSettlements.AsNoTracking()
            .Where(s => s.Origin == FinancialSettlementOrigin.AdvanceApplication));
    }

    [Fact]
    public async Task Above_the_credit_is_refused()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(() => seed.ApplyAsync(10_000.01m));

        Assert.Equal("Valor acima do crédito disponível do adiantamento FN-ADT-1 (10.000,00).", error.Message);
    }

    [Fact]
    public async Task Unpaid_advance_has_no_credit()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync(advancePaid: 0m);

        var error = await Assert.ThrowsAsync<ApplicationException>(() => seed.ApplyAsync(100m));

        Assert.Equal("Valor acima do crédito disponível do adiantamento FN-ADT-1 (0,00).", error.Message);
    }

    [Fact]
    public async Task Second_application_beyond_the_remaining_credit_is_refused()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();
        await seed.ApplyAsync(6_000m);

        var error = await Assert.ThrowsAsync<ApplicationException>(() => seed.ApplyAsync(5_000m));

        Assert.Equal("Valor acima do crédito disponível do adiantamento FN-ADT-1 (4.000,00).", error.Message);
        Assert.Equal(6_000m, (await seed.ReloadAsync(seed.Advance.Key)).AppliedAmount);
    }

    [Fact]
    public async Task Advance_of_another_contract_is_refused()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();
        var other = await seed.Firm.Fiscal.AddContractAsync("PC-2");
        var foreign = await seed.AddAdvanceAsync(other, 5_000m, 5_000m, "FN-ADT-2");

        var error = await Assert.ThrowsAsync<ApplicationException>(() => seed.ApplyAsync(1_000m, advance: foreign));

        Assert.Equal("Adiantamento FN-ADT-2 não pertence ao contrato do título FN-PROV-1.", error.Message);
    }

    [Fact]
    public async Task Different_currency_is_refused()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();
        var dollar = await seed.AddAdvanceAsync(seed.Contract, 5_000m, 0m, "FN-ADT-2", CurrencyType.Usd);

        var error = await Assert.ThrowsAsync<ApplicationException>(() => seed.ApplyAsync(1_000m, advance: dollar));

        Assert.Equal(
            "O adiantamento FN-ADT-2 está em US$ e o título FN-PROV-1 em R$: compensação entre moedas diferentes não é suportada.",
            error.Message);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("0.004")]
    public async Task Non_positive_amount_is_refused(string amount)
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(() =>
            seed.ApplyAsync(decimal.Parse(amount, CultureInfo.InvariantCulture)));

        Assert.Equal("Informe um valor maior que zero.", error.Message);
    }

    [Fact]
    public async Task Amount_is_rounded_to_cents_before_the_ceilings()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();

        await seed.ApplyAsync(4_000.004m);

        Assert.Equal(4_000m, (await seed.LineAsync(seed.Advance.Key, FinancialSettlementOrigin.AdvanceApplication)).Amount);
        Assert.Equal(4_000m, (await seed.ReloadAsync(seed.Advance.Key)).AppliedAmount);
    }

    [Fact]
    public async Task Canceled_provisional_is_refused()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();
        seed.Provisional.Status = FinancialDocumentStatus.Canceled;
        await seed.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(() => seed.ApplyAsync(1_000m));

        Assert.Equal("Título cancelado não pode ser compensado.", error.Message);
    }

    [Fact]
    public async Task Canceled_advance_is_refused()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();
        var canceled = await seed.AddAdvanceAsync(seed.Contract, 5_000m, 0m, "FN-ADT-2");
        canceled.Status = FinancialDocumentStatus.Canceled;
        await seed.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(() => seed.ApplyAsync(1_000m, advance: canceled));

        Assert.Equal("Título cancelado não pode ser compensado.", error.Message);
    }

    [Fact]
    public async Task Only_a_purchase_provisional_receives_the_application()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(() =>
            seed.ApplyAsync(1_000m, provisional: seed.ComposedFirm));

        Assert.Equal("Título ST-0001 não é provisório de contrato de compra.", error.Message);
    }

    [Fact]
    public async Task Only_an_advance_can_be_applied()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(() =>
            seed.ApplyAsync(1_000m, advance: seed.ComposedFirm));

        Assert.Equal("O título ST-0001 não é um adiantamento.", error.Message);
    }
}
