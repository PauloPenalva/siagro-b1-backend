using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>Spec 2026-10-14, regra 10 (D2): encerrar o contrato de compra exige crédito de adiantamento zero.</summary>
public class PurchaseContractsCloseAdvanceCreditTests
{
    private static PurchaseContractsCloseService Close(AdvanceApplicationSeed s) =>
        new(s.Context, new PurchaseContractsFixedVolumeService(s.Context), TestNotificationOutbox.For(s.Context),
            TestConfiguration.Empty(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PurchaseContractsCloseService>.Instance);

    /// <summary>Fixação confirmada do provisório do seed (mesma do FinancialDocumentFirmLocksTests).</summary>
    private static async Task AddFixationAsync(AdvanceApplicationSeed s)
    {
        s.Context.PurchaseContractsPriceFixations.Add(new PurchaseContractPriceFixation
        {
            Key = s.Provisional.OriginKey!.Value, PurchaseContractKey = s.Contract.Key, FixationVolume = 9_864.165m,
            FixationPrice = 2m, Status = PriceFixationStatus.Confirmed, FinancialDueDate = new DateTime(2026, 12, 31),
        });
        await s.Context.SaveChangesAsync();
    }

    /// <summary>Provisório de 19.728,33 quitado por compensação de um adiantamento pago de <paramref name="advance"/>.</summary>
    private static async Task<AdvanceApplicationSeed> SettledByAdvanceAsync(decimal advance)
    {
        var seed = await AdvanceApplicationSeed.CreateAsync(
            provisionalAmount: FirmPayableSeed.DefaultFirmAmount, advanceAmount: advance, advancePaid: advance);
        await AddFixationAsync(seed);
        await seed.ApplyAsync(FirmPayableSeed.DefaultFirmAmount);
        return seed;
    }

    [Fact]
    public async Task Closing_with_credit_left_is_refused()
    {
        var seed = await SettledByAdvanceAsync(20_000m);

        var error = await Assert.ThrowsAsync<ApplicationException>(() => Close(seed).ExecuteAsync(seed.Contract.Key, "ana"));

        Assert.Equal(
            "O adiantamento FN-ADT-1 tem 271,67 de crédito não aplicado: compense, devolva ou vincule a outro contrato antes de encerrar.",
            error.Message);
        Assert.Equal(ContractStatus.Approved, seed.Contract.Status);
    }

    [Fact]
    public async Task Closing_after_applying_all_the_credit_passes()
    {
        var seed = await SettledByAdvanceAsync(FirmPayableSeed.DefaultFirmAmount);

        await Close(seed).ExecuteAsync(seed.Contract.Key, "ana");

        Assert.Equal(ContractStatus.Finished, seed.Contract.Status);
    }

    [Fact]
    public async Task Closing_after_refunding_the_credit_passes()
    {
        var seed = await SettledByAdvanceAsync(20_000m);
        await new FinancialAdvancesRefundService(seed.Db).ExecuteAsync(
            seed.Advance.Key, "CX01", new DateTime(2026, 10, 20), null, "sobra devolvida", "ana");

        await Close(seed).ExecuteAsync(seed.Contract.Key, "ana");

        Assert.Equal(ContractStatus.Finished, seed.Contract.Status);
        Assert.Equal(FinancialDocumentStatus.PartiallySettled, (await seed.ReloadAsync(seed.Advance.Key)).Status);
    }

    [Fact]
    public async Task Closing_after_relinking_the_credit_passes()
    {
        var seed = await SettledByAdvanceAsync(20_000m);
        var target = await seed.Firm.Fiscal.AddContractAsync("PC-2");
        await new FinancialAdvancesRelinkContractService(
                seed.Db, new FinancialDocumentChangeLogService(seed.Context), seed.Firm.Numbers)
            .ExecuteAsync(seed.Advance.Key, "Purchase", target.Key, "ana");

        await Close(seed).ExecuteAsync(seed.Contract.Key, "ana");

        Assert.Equal(ContractStatus.Finished, seed.Contract.Status);
    }

    [Fact]
    public async Task Unpaid_advance_does_not_block_the_closing()
    {
        var seed = await SettledByAdvanceAsync(FirmPayableSeed.DefaultFirmAmount);
        await seed.AddAdvanceAsync(seed.Contract, 5_000m, 0m, "FN-ADT-2");

        await Close(seed).ExecuteAsync(seed.Contract.Key, "ana");

        Assert.Equal(ContractStatus.Finished, seed.Contract.Status);
    }
}
