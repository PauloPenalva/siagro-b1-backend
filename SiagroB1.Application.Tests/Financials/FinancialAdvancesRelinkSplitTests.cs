using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>
/// Spec 2026-10-14, regras 8–9 (D3): revincular adiantamento parcialmente aplicado DESMEMBRA — o crédito vai para um
/// adiantamento novo no outro contrato; estornar a transferência cancela o novo, se intocado.
/// </summary>
public class FinancialAdvancesRelinkSplitTests
{
    private static FinancialAdvancesRelinkContractService Relink(AdvanceApplicationSeed s) =>
        new(s.Db, new FinancialDocumentChangeLogService(s.Context), s.Firm.Numbers);

    /// <summary>Compensa 3.000 do FN-ADT-1 (10.000 pago) e revincula a sobra ao PC-2: nasce o ST-0002 com 7.000.</summary>
    private static async Task<(PurchaseContract target, FinancialDocument split)> SplitAsync(AdvanceApplicationSeed seed)
    {
        await seed.ApplyAsync(3_000m);
        var target = await seed.Firm.Fiscal.AddContractAsync("PC-2");

        await Relink(seed).ExecuteAsync(seed.Advance.Key, "Purchase", target.Key, "ana");

        var split = await seed.Context.FinancialDocuments.AsNoTracking().SingleAsync(d =>
            d.OriginType == FinancialDocumentOrigin.FinancialDocument && d.OriginKey == seed.Advance.Key);
        return (target, split);
    }

    private static Task<FinancialSettlement> OutgoingTransferAsync(AdvanceApplicationSeed seed) =>
        seed.LineAsync(seed.Advance.Key, FinancialSettlementOrigin.AdvanceTransfer);

    [Fact]
    public async Task Relinking_an_applied_advance_splits_the_credit_into_a_new_paid_advance()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();

        var (target, split) = await SplitAsync(seed);

        Assert.Equal("ST-0002", split.Code);
        Assert.Equal(FinancialDocumentNature.Advance, split.Nature);
        Assert.Equal(FinancialDirection.Payable, split.Direction);
        Assert.Equal(7_000m, split.NetAmount);
        Assert.Equal(7_000m, split.SettledAmount);
        Assert.Equal(0m, split.AppliedAmount);
        Assert.Equal(7_000m, split.AvailableAdvanceAmount);
        Assert.Equal(FinancialDocumentStatus.Settled, split.Status);
        Assert.Equal(target.Key, split.PurchaseContractKey);
        Assert.Equal("PC-2", split.OriginDocNumber);
        Assert.Equal(seed.Advance.CardCode, split.CardCode);
        Assert.Equal(seed.Advance.Currency, split.Currency);
        Assert.Equal(seed.Advance.DueDate, split.DueDate);
        Assert.Equal("Depósito no Banco X", split.PaymentTermsText);
        Assert.Equal("Crédito não aplicado do adiantamento FN-ADT-1.", split.Comments);

        var original = await seed.ReloadAsync(seed.Advance.Key);
        Assert.Equal(seed.Contract.Key, original.PurchaseContractKey);
        Assert.Equal("PC-1", original.OriginDocNumber);
        Assert.Equal(10_000m, original.SettledAmount);
        Assert.Equal(10_000m, original.AppliedAmount);
        Assert.Equal(0m, original.AvailableAdvanceAmount);

        var outgoing = await OutgoingTransferAsync(seed);
        Assert.Equal(7_000m, outgoing.Amount);
        Assert.Null(outgoing.FinancialAccountCode);
        Assert.Equal(split.Key, outgoing.RelatedDocumentKey);
        Assert.Equal("ST-0002", outgoing.DocumentReference);

        var incoming = await seed.LineAsync(split.Key, FinancialSettlementOrigin.AdvanceTransfer);
        Assert.Equal(7_000m, incoming.Amount);
        Assert.Equal(seed.Advance.Key, incoming.RelatedDocumentKey);
        Assert.Equal("FN-ADT-1", incoming.DocumentReference);

        var logs = await seed.Context.FinancialDocumentChangeLogs.AsNoTracking()
            .Where(l => l.Field == FinancialDocumentChangeLogFields.AdvanceTransfer).ToListAsync();
        Assert.Equal("ST-0002: 7.000,00", logs.Single(l => l.FinancialDocumentKey == seed.Advance.Key).NewValue);
        Assert.Equal("FN-ADT-1: 7.000,00", logs.Single(l => l.FinancialDocumentKey == split.Key).NewValue);
    }

    [Fact]
    public async Task Relinking_without_application_still_moves_the_whole_advance()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();
        var target = await seed.Firm.Fiscal.AddContractAsync("PC-2");

        await Relink(seed).ExecuteAsync(seed.Advance.Key, "Purchase", target.Key, "ana");

        Assert.Equal(target.Key, (await seed.ReloadAsync(seed.Advance.Key)).PurchaseContractKey);
        Assert.Empty(seed.Context.FinancialDocuments.AsNoTracking()
            .Where(d => d.OriginType == FinancialDocumentOrigin.FinancialDocument));
    }

    [Fact]
    public async Task Splitting_without_credit_is_refused()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();
        await seed.ApplyAsync(10_000m);
        var target = await seed.Firm.Fiscal.AddContractAsync("PC-2");

        var error = await Assert.ThrowsAsync<ApplicationException>(() =>
            Relink(seed).ExecuteAsync(seed.Advance.Key, "Purchase", target.Key, "ana"));

        Assert.Equal("O adiantamento FN-ADT-1 não tem crédito disponível para vincular a outro contrato.", error.Message);
    }

    [Fact]
    public async Task Reversing_the_transfer_cancels_the_split_advance_and_returns_the_credit()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();
        var (_, split) = await SplitAsync(seed);

        await seed.Reverse().ExecuteAsync((await OutgoingTransferAsync(seed)).Key, "revínculo errado", "bia");

        var canceled = await seed.ReloadAsync(split.Key);
        Assert.Equal(FinancialDocumentStatus.Canceled, canceled.Status);
        Assert.Equal("Transferência de crédito estornada", canceled.CancellationReason);
        Assert.Equal(0m, canceled.SettledAmount);

        var original = await seed.ReloadAsync(seed.Advance.Key);
        Assert.Equal(3_000m, original.AppliedAmount);
        Assert.Equal(7_000m, original.AvailableAdvanceAmount);
    }

    [Fact]
    public async Task Reversing_the_transfer_of_a_used_split_advance_is_refused()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();
        var (target, split) = await SplitAsync(seed);
        var c2 = await seed.Firm.ConciliateAsync(contract: target, taxNumber: "1002");
        var provisional2 = await seed.Firm.AddProvisionalAsync(target, 50_000m, "FN-PROV-2");
        await seed.ComposeAsync(provisional2, c2.Firm!);
        await seed.ApplyAsync(2_000m, advance: split, provisional: provisional2);

        var error = await Assert.ThrowsAsync<ApplicationException>(async () =>
            await seed.Reverse().ExecuteAsync((await OutgoingTransferAsync(seed)).Key, "revínculo errado", "bia"));

        Assert.Equal(
            "O adiantamento ST-0002 já foi usado: desfaça as compensações dele antes de estornar a transferência.",
            error.Message);
    }

    [Fact]
    public async Task Undone_applications_free_the_transfer_reversal()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();
        var (target, split) = await SplitAsync(seed);
        var c2 = await seed.Firm.ConciliateAsync(contract: target, taxNumber: "1002");
        var provisional2 = await seed.Firm.AddProvisionalAsync(target, 50_000m, "FN-PROV-2");
        await seed.ComposeAsync(provisional2, c2.Firm!);
        await seed.ApplyAsync(2_000m, advance: split, provisional: provisional2);
        var application = await seed.LineAsync(split.Key, FinancialSettlementOrigin.AdvanceApplication);
        await seed.Reverse().ExecuteAsync(application.Key, "compensação errada", "bia");

        await seed.Reverse().ExecuteAsync((await OutgoingTransferAsync(seed)).Key, "revínculo errado", "bia");

        Assert.Equal(FinancialDocumentStatus.Canceled, (await seed.ReloadAsync(split.Key)).Status);
        Assert.Equal(7_000m, (await seed.ReloadAsync(seed.Advance.Key)).AvailableAdvanceAmount);
    }

    [Fact]
    public async Task Refunded_split_advance_blocks_the_transfer_reversal()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();
        var (_, split) = await SplitAsync(seed);
        await new FinancialAdvancesRefundService(seed.Db).ExecuteAsync(
            split.Key, "CX01", new DateTime(2026, 10, 20), null, "produtor pediu de volta", "ana");

        var error = await Assert.ThrowsAsync<ApplicationException>(async () =>
            await seed.Reverse().ExecuteAsync((await OutgoingTransferAsync(seed)).Key, "revínculo errado", "bia"));

        Assert.Equal("Não é possível estornar a baixa de um título cancelado.", error.Message);
    }

    [Fact]
    public async Task Reversing_the_payment_of_a_split_advance_is_refused()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();
        await SplitAsync(seed);
        var payment = await seed.LineAsync(seed.Advance.Key, FinancialSettlementOrigin.Manual);

        var error = await Assert.ThrowsAsync<ApplicationException>(() =>
            seed.Reverse().ExecuteAsync(payment.Key, "pagamento errado", "bia"));

        Assert.Equal(
            "O adiantamento FN-ADT-1 tem 10.000,00 aplicado: desfaça as compensações antes de estornar o pagamento.",
            error.Message);
    }
}
