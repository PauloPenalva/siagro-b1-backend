using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>Spec 2026-10-14, regras 5–6: o "Estornar baixa" desfaz a compensação em par e trava o estorno do pagamento.</summary>
public class FinancialDocumentsAdvanceApplicationReverseTests
{
    [Fact]
    public async Task Reversing_the_advance_line_reverses_the_pair_and_restores_both()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();
        await seed.ApplyAsync(6_000m);
        var onAdvance = await seed.LineAsync(seed.Advance.Key, FinancialSettlementOrigin.AdvanceApplication);
        var onProvisional = await seed.LineAsync(seed.Provisional.Key, FinancialSettlementOrigin.AdvanceApplication);

        await seed.Reverse().ExecuteAsync(onAdvance.Key, "compensação errada", "bia");

        var reversals = await seed.Context.FinancialSettlements.AsNoTracking()
            .Where(s => s.Origin == FinancialSettlementOrigin.Reversal).ToListAsync();
        Assert.Equal(2, reversals.Count);
        Assert.Contains(reversals, r => r.ReversedSettlementKey == onAdvance.Key && r.Amount == -6_000m);
        Assert.Contains(reversals, r => r.ReversedSettlementKey == onProvisional.Key && r.Amount == -6_000m);

        var provisional = await seed.ReloadAsync(seed.Provisional.Key);
        Assert.Equal(0m, provisional.SettledAmount);
        Assert.Equal(19_728.33m, provisional.AvailableToSettleAmount);
        Assert.Equal(FinancialDocumentStatus.Open, provisional.Status);

        var advance = await seed.ReloadAsync(seed.Advance.Key);
        Assert.Equal(10_000m, advance.SettledAmount);
        Assert.Equal(0m, advance.AppliedAmount);
        Assert.Equal(10_000m, advance.AvailableAdvanceAmount);

        var undoLogs = await seed.Context.FinancialDocumentChangeLogs.AsNoTracking()
            .Where(l => l.Field == FinancialDocumentChangeLogFields.AdvanceApplication && l.NewValue == null)
            .ToListAsync();
        Assert.Equal(2, undoLogs.Count);
        Assert.Contains(undoLogs, l => l.FinancialDocumentKey == seed.Provisional.Key && l.OldValue == "FN-ADT-1: 6.000,00");
        Assert.Contains(undoLogs, l => l.FinancialDocumentKey == seed.Advance.Key && l.OldValue == "FN-PROV-1: 6.000,00");
    }

    [Fact]
    public async Task Reversing_from_the_provisional_side_reverses_the_pair_too()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();
        await seed.ApplyAsync(6_000m);
        var onProvisional = await seed.LineAsync(seed.Provisional.Key, FinancialSettlementOrigin.AdvanceApplication);

        await seed.Reverse().ExecuteAsync(onProvisional.Key, "compensação errada", "bia");

        Assert.Equal(0m, (await seed.ReloadAsync(seed.Advance.Key)).AppliedAmount);
        Assert.Equal(0m, (await seed.ReloadAsync(seed.Provisional.Key)).SettledAmount);
    }

    [Fact]
    public async Task Reversing_the_payment_of_an_applied_advance_is_refused()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();
        await seed.ApplyAsync(6_000m);
        var payment = await seed.LineAsync(seed.Advance.Key, FinancialSettlementOrigin.Manual);

        var error = await Assert.ThrowsAsync<ApplicationException>(() =>
            seed.Reverse().ExecuteAsync(payment.Key, "pagamento errado", "bia"));

        Assert.Equal(
            "O adiantamento FN-ADT-1 tem 6.000,00 aplicado: desfaça as compensações antes de estornar o pagamento.",
            error.Message);
    }

    [Fact]
    public async Task Reversing_one_of_two_payments_passes_while_the_rest_covers_the_application()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync(advancePaid: 5_000m);
        await seed.PayAsync(seed.Advance, 5_000m);
        await seed.ApplyAsync(4_000m);
        var payment = await seed.Context.FinancialSettlements.AsNoTracking()
            .FirstAsync(s => s.FinancialDocumentKey == seed.Advance.Key && s.Origin == FinancialSettlementOrigin.Manual);

        await seed.Reverse().ExecuteAsync(payment.Key, "pagamento em duplicidade", "bia");

        var advance = await seed.ReloadAsync(seed.Advance.Key);
        Assert.Equal(5_000m, advance.SettledAmount);
        Assert.Equal(4_000m, advance.AppliedAmount);
        Assert.Equal(1_000m, advance.AvailableAdvanceAmount);
    }

    [Fact]
    public async Task Reversing_a_payment_without_application_still_works()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();
        var payment = await seed.LineAsync(seed.Advance.Key, FinancialSettlementOrigin.Manual);

        await seed.Reverse().ExecuteAsync(payment.Key, "pagamento errado", "bia");

        var advance = await seed.ReloadAsync(seed.Advance.Key);
        Assert.Equal(0m, advance.SettledAmount);
        Assert.Equal(FinancialDocumentStatus.Open, advance.Status);
    }

    [Fact]
    public async Task Pending_application_rows_and_their_reversals_in_the_same_unit_of_work_leave_applied_at_zero()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();
        var original = new FinancialSettlement
        {
            Key = Guid.NewGuid(), FinancialDocumentKey = seed.Advance.Key, RelatedDocumentKey = seed.Provisional.Key,
            Origin = FinancialSettlementOrigin.AdvanceApplication, Amount = 6_000m, SettlementDate = DateTime.Today,
            CreatedBy = "ana", UpdatedBy = "ana",
        };
        var reversal = new FinancialSettlement
        {
            Key = Guid.NewGuid(), FinancialDocumentKey = seed.Advance.Key, RelatedDocumentKey = seed.Provisional.Key,
            Origin = FinancialSettlementOrigin.Reversal, ReversedSettlementKey = original.Key, Amount = -6_000m,
            SettlementDate = DateTime.Today, CreatedBy = "ana", UpdatedBy = "ana",
        };
        seed.Context.FinancialSettlements.AddRange(original, reversal);

        await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(seed.Context, seed.Advance.Key);

        var advance = await seed.Context.FinancialDocuments.FindAsync(seed.Advance.Key);
        Assert.Equal(0m, advance!.AppliedAmount);
        Assert.Equal(10_000m, advance.SettledAmount);
    }

    [Fact]
    public async Task A_stored_application_row_marked_deleted_is_subtracted_from_applied()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();
        await seed.ApplyAsync(6_000m);
        var line = await seed.Context.FinancialSettlements
            .SingleAsync(s => s.FinancialDocumentKey == seed.Advance.Key &&
                              s.Origin == FinancialSettlementOrigin.AdvanceApplication);
        seed.Context.FinancialSettlements.Remove(line);

        await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(seed.Context, seed.Advance.Key);

        var advance = await seed.Context.FinancialDocuments.FindAsync(seed.Advance.Key);
        Assert.Equal(0m, advance!.AppliedAmount);
    }
}
