using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>Spec 2026-10-14, regra 7: a devolução leva só o crédito disponível (pago − aplicado).</summary>
public class FinancialAdvancesRefundApplicationTests
{
    private static Task RefundAsync(AdvanceApplicationSeed s) =>
        new FinancialAdvancesRefundService(s.Db).ExecuteAsync(
            s.Advance.Key, "CX01", new DateTime(2026, 10, 20), "TED-2", "sobra devolvida", "ana");

    [Fact]
    public async Task Refund_with_application_returns_only_the_credit_and_keeps_the_document()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();
        await seed.ApplyAsync(6_000m);

        await RefundAsync(seed);

        var refund = await seed.LineAsync(seed.Advance.Key, FinancialSettlementOrigin.AdvanceRefund);
        Assert.Equal(-4_000m, refund.Amount);
        Assert.Equal("CX01", refund.FinancialAccountCode);

        var advance = await seed.ReloadAsync(seed.Advance.Key);
        Assert.Equal(6_000m, advance.SettledAmount);
        Assert.Equal(6_000m, advance.AppliedAmount);
        Assert.Equal(0m, advance.AvailableAdvanceAmount);
        Assert.Equal(FinancialDocumentStatus.PartiallySettled, advance.Status);
        Assert.Null(advance.CancellationReason);
    }

    [Fact]
    public async Task Refund_without_application_returns_everything_and_cancels()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();

        await RefundAsync(seed);

        Assert.Equal(-10_000m, (await seed.LineAsync(seed.Advance.Key, FinancialSettlementOrigin.AdvanceRefund)).Amount);
        var advance = await seed.ReloadAsync(seed.Advance.Key);
        Assert.Equal(FinancialDocumentStatus.Canceled, advance.Status);
        Assert.Equal(0m, advance.AvailableAdvanceAmount);
    }

    [Fact]
    public async Task Fully_applied_advance_has_nothing_to_refund()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();
        await seed.ApplyAsync(10_000m);

        var error = await Assert.ThrowsAsync<ApplicationException>(() => RefundAsync(seed));

        Assert.Equal("O adiantamento FN-ADT-1 não tem crédito disponível para devolver.", error.Message);
    }
}
