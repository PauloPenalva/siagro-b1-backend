using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Infra.Interceptors;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>Spec 2026-10-13 (títulos firmes), regras 25–30 e D6/D8/D9.</summary>
public class FinancialDocumentFirmLocksTests
{
    private static Task ComposeAsync(FirmPayableSeed s, Guid firmKey) =>
        new FinancialDocumentsComposeService(s.Db, new FinancialDocumentChangeLogService(s.Context))
            .ExecuteAsync(s.Provisional.Key, [firmKey], "ana");

    private static Task SettleProvisionalAsync(FirmPayableSeed s, decimal amount) =>
        new FinancialDocumentsSettleService(s.Db)
            .ExecuteAsync(s.Provisional.Key, "CX01", amount, DateTime.Today, 0m, 0m, 0m, null, null, "ana");

    /// <summary>Fixação confirmada do provisório do seed: 50.000 × 2,00 = 100.000,00.</summary>
    private static async Task<PurchaseContractPriceFixation> AddFixationAsync(FirmPayableSeed s, decimal volume = 50_000m)
    {
        var fixation = new PurchaseContractPriceFixation
        {
            Key = s.Provisional.OriginKey!.Value, PurchaseContractKey = s.Contract.Key, FixationVolume = volume,
            FixationPrice = 2m, Status = PriceFixationStatus.Confirmed, FinancialDueDate = new DateTime(2026, 12, 31),
        };
        s.Context.PurchaseContractsPriceFixations.Add(fixation);
        await s.Context.SaveChangesAsync();
        return fixation;
    }

    /// <summary>Composição gravada direto (sem conciliação): o cancelamento do contrato exige contrato sem alocação.</summary>
    private static async Task SeedCompositionAsync(FirmPayableSeed s, decimal amount)
    {
        s.Context.FinancialDocumentCompositions.Add(new FinancialDocumentComposition
        {
            Key = Guid.NewGuid(), ProvisionalDocumentKey = s.Provisional.Key, ComposedDocumentKey = Guid.NewGuid(), Amount = amount,
        });
        await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(s.Context, s.Provisional.Key);
        await s.Context.SaveChangesAsync();
    }

    private static PurchaseContractsCloseService Close(FirmPayableSeed s, string? cutover = null) =>
        new(s.Context, new PurchaseContractsFixedVolumeService(s.Context), TestNotificationOutbox.For(s.Context),
            TestConfiguration.With(("Financial:FirmPayablesCutoverDate", cutover)),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PurchaseContractsCloseService>.Instance);

    private const string UnsettledProvisional =
        "O título FN-PROV-1 não está quitado: o contrato só encerra com todos os títulos quitados. Entrega a menos se resolve por washout; faturamento abaixo, por documento de complemento de valor.";

    /// <summary>D13: o provisório do seed nasce em 01/09/2026 — "antes da data de corte" quando o corte é 01/10/2026.</summary>
    private static async Task BackdateProvisionalAsync(FirmPayableSeed s)
    {
        s.Provisional.CreatedAt = new DateTime(2026, 9, 1);
        await s.Context.SaveChangesAsync();
    }

    [Fact]
    public async Task Reversing_a_fixation_whose_provisional_has_composition_is_refused_until_uncomposed()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        var fixation = await AddFixationAsync(seed);
        var c = await seed.ConciliateAsync();
        await ComposeAsync(seed, c.Firm!.Key);
        var service = new PurchaseContractsPriceFixationsCancelService(
            seed.Context, new PurchaseContractsFixedVolumeService(seed.Context), new PurchaseContractsChangeLogService(seed.Context),
            TestNotificationOutbox.For(seed.Context), FinancialDocumentTestServices.Cancel(seed.Context));

        var error = await Assert.ThrowsAsync<ApplicationException>(() => service.ExecuteAsync(fixation.Key, "ana"));
        Assert.Equal("Desfaça a composição do título FN-PROV-1 antes de estornar a fixação.", error.Message);

        var row = await seed.Context.FinancialDocumentCompositions.AsNoTracking().SingleAsync();
        await new FinancialDocumentsUncomposeService(seed.Db).ExecuteAsync(row.Key, "ana");
        await service.ExecuteAsync(fixation.Key, "ana");

        Assert.Equal(FinancialDocumentStatus.Canceled, (await seed.ReloadAsync(seed.Provisional.Key)).Status);
        Assert.Equal(FinancialDocumentStatus.Open, (await seed.ReloadAsync(c.Firm.Key)).Status); // D6: firme volta solto
    }

    [Fact]
    public async Task Washout_cannot_take_the_provisional_below_the_composed_nor_cancel_it()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        var fixation = await AddFixationAsync(seed);
        var c = await seed.ConciliateAsync();
        await ComposeAsync(seed, c.Firm!.Key);
        var adjust = new FinancialDocumentsAdjustProvisionalService(
            seed.Context, FinancialDocumentTestServices.Generate(seed.Context), new FinancialDocumentChangeLogService(seed.Context));

        var below = await Assert.ThrowsAsync<ApplicationException>(() =>
            adjust.EnqueueForPurchaseFixationAsync(seed.Contract, fixation, 5_000m, "ana"));
        Assert.Equal(
            "O washout reduziria o título FN-PROV-1 para 10.000,00, abaixo do valor já composto (19.728,33): desfaça a composição antes.",
            below.Message);

        var cancel = await Assert.ThrowsAsync<ApplicationException>(() =>
            adjust.EnqueueForPurchaseFixationAsync(seed.Contract, fixation, 0m, "ana"));
        Assert.Equal(
            "O washout consumiria toda a fixação, mas o título FN-PROV-1 tem composição ou baixa: desfaça a composição e estorne as baixas antes.",
            cancel.Message);
    }

    [Fact]
    public async Task Washout_down_to_the_paid_amount_settles_the_provisional()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        var fixation = await AddFixationAsync(seed);
        var c = await seed.ConciliateAsync();
        await ComposeAsync(seed, c.Firm!.Key);
        await SettleProvisionalAsync(seed, FirmPayableSeed.DefaultFirmAmount);
        var adjust = new FinancialDocumentsAdjustProvisionalService(
            seed.Context, FinancialDocumentTestServices.Generate(seed.Context), new FinancialDocumentChangeLogService(seed.Context));

        await adjust.EnqueueForPurchaseFixationAsync(seed.Contract, fixation, 9_864.165m, "ana"); // × 2,00 = 19.728,33
        await seed.Context.SaveChangesAsync();

        var provisional = await seed.ReloadAsync(seed.Provisional.Key);
        Assert.Equal(FirmPayableSeed.DefaultFirmAmount, provisional.NetAmount);
        Assert.Equal(FinancialDocumentStatus.Settled, provisional.Status);
    }

    [Fact]
    public async Task Cancelling_the_contract_or_withdrawing_approval_with_composition_is_refused()
    {
        const string expected =
            "O título FN-PROV-1 tem composição ou baixa: desfaça a composição e estorne as baixas antes de cancelar o contrato ou retirar a aprovação.";
        var seed = await FirmPayableSeed.CreateAsync();
        await SeedCompositionAsync(seed, 500m);

        var cancel = await Assert.ThrowsAsync<ApplicationException>(() =>
            new PurchaseContractsCancelService(seed.Db, TestNotificationOutbox.For(seed.Context),
                    FinancialDocumentTestServices.Cancel(seed.Context), FinancialDocumentTestServices.CancellationGuard(seed.Context))
                .ExecuteAsync(seed.Contract.Key, "teste", "ana"));
        Assert.Equal(expected, cancel.Message);

        seed.Context.ChangeTracker.Clear();
        var withdraw = await Assert.ThrowsAsync<ApplicationException>(() =>
            new PurchaseContractsWithdrawApprovalService(seed.Db, new FakeStringLocalizer<Resource>(),
                    TestNotificationOutbox.For(seed.Context), FinancialDocumentTestServices.Cancel(seed.Context))
                .ExecuteAsync(seed.Contract.Key, "ana"));
        Assert.Equal(expected, withdraw.Message);

        Assert.Equal(ContractStatus.Approved,
            (await seed.Context.PurchaseContracts.AsNoTracking().SingleAsync(x => x.Key == seed.Contract.Key)).Status);
    }

    [Fact]
    public async Task Manual_cancel_of_a_provisional_with_composition_is_refused()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        await SeedCompositionAsync(seed, 500m);

        var error = await Assert.ThrowsAsync<ApplicationException>(() =>
            new FinancialDocumentsCancelService(seed.Db).ExecuteAsync(seed.Provisional.Key, "teste", "ana"));

        Assert.Equal("O documento FN-PROV-1 tem composição. Desfaça a composição antes de cancelar.", error.Message);
    }

    [Fact]
    public async Task Closing_with_a_firm_with_balance_is_refused()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        await seed.ConciliateAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(() => Close(seed).ExecuteAsync(seed.Contract.Key, "ana"));

        Assert.Equal("Há títulos firmes com saldo neste contrato: componha, compense ou baixe por dação antes de encerrar.", error.Message);
    }

    [Fact]
    public async Task Closing_with_an_unsettled_provisional_is_refused()
    {
        var seed = await FirmPayableSeed.CreateAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(() => Close(seed).ExecuteAsync(seed.Contract.Key, "ana"));

        Assert.Equal(
            "O título FN-PROV-1 não está quitado: o contrato só encerra com todos os títulos quitados. Entrega a menos se resolve por washout; faturamento abaixo, por documento de complemento de valor.",
            error.Message);
    }

    [Fact]
    public async Task Closing_cancels_a_legacy_untouched_provisional_created_before_the_cutover()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        await BackdateProvisionalAsync(seed);

        await Close(seed, "2026-10-01").ExecuteAsync(seed.Contract.Key, "ana");

        var provisional = await seed.ReloadAsync(seed.Provisional.Key);
        Assert.Equal(FinancialDocumentStatus.Canceled, provisional.Status);
        Assert.Equal("Encerramento do contrato", provisional.CancellationReason);
        Assert.Equal("ana", provisional.CanceledBy);
        Assert.Equal(ContractStatus.Finished,
            (await seed.Context.PurchaseContracts.AsNoTracking().SingleAsync(x => x.Key == seed.Contract.Key)).Status);
    }

    [Fact]
    public async Task Closing_refuses_an_untouched_provisional_created_after_the_cutover()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        seed.Provisional.CreatedAt = new DateTime(2026, 10, 2);
        await seed.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(() =>
            Close(seed, "2026-10-01").ExecuteAsync(seed.Contract.Key, "ana"));

        Assert.Equal(UnsettledProvisional, error.Message);
    }

    [Fact]
    public async Task Closing_refuses_a_provisional_before_the_cutover_with_composition()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        await BackdateProvisionalAsync(seed);
        await SeedCompositionAsync(seed, 100m);

        var error = await Assert.ThrowsAsync<ApplicationException>(() =>
            Close(seed, "2026-10-01").ExecuteAsync(seed.Contract.Key, "ana"));

        Assert.Equal(UnsettledProvisional, error.Message);
    }

    [Fact]
    public async Task Closing_refuses_a_provisional_before_the_cutover_with_settlement()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        await BackdateProvisionalAsync(seed);
        seed.Context.FinancialSettlements.Add(new FinancialSettlement
        {
            Key = Guid.NewGuid(), FinancialDocumentKey = seed.Provisional.Key, FinancialAccountCode = "CX01", Amount = 100m,
            SettlementDate = DateTime.Today, Origin = FinancialSettlementOrigin.Manual,
        });
        await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(seed.Context, seed.Provisional.Key);
        await seed.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(() =>
            Close(seed, "2026-10-01").ExecuteAsync(seed.Contract.Key, "ana"));

        Assert.Equal(UnsettledProvisional, error.Message);
    }

    [Fact]
    public async Task Closing_without_the_cutover_key_exempts_nothing()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        await BackdateProvisionalAsync(seed);

        var error = await Assert.ThrowsAsync<ApplicationException>(() => Close(seed).ExecuteAsync(seed.Contract.Key, "ana"));

        Assert.Equal(UnsettledProvisional, error.Message);
        Assert.Equal(FinancialDocumentStatus.Open, (await seed.ReloadAsync(seed.Provisional.Key)).Status);
    }

    [Fact]
    public async Task Closing_and_reopening_a_fully_settled_contract_keep_every_value()
    {
        var seed = await FirmPayableSeed.CreateAsync(provisionalAmount: FirmPayableSeed.DefaultFirmAmount);
        await AddFixationAsync(seed, volume: 9_864.165m);
        var c = await seed.ConciliateAsync();
        await ComposeAsync(seed, c.Firm!.Key);
        await SettleProvisionalAsync(seed, FirmPayableSeed.DefaultFirmAmount);

        await Close(seed).ExecuteAsync(seed.Contract.Key, "ana");

        var closed = await seed.ReloadAsync(seed.Provisional.Key);
        Assert.Equal(FinancialDocumentStatus.Settled, closed.Status);
        Assert.Equal(FirmPayableSeed.DefaultFirmAmount, closed.NetAmount);

        await new PurchaseContractsReopenService(seed.Context, TestNotificationOutbox.For(seed.Context),
                FinancialDocumentTestServices.Generate(seed.Context))
            .ExecuteAsync(seed.Contract.Key, "ana");

        var provisionals = await seed.Context.FinancialDocuments.AsNoTracking()
            .Where(d => d.PurchaseContractKey == seed.Contract.Key && d.Nature == FinancialDocumentNature.Provisional)
            .ToListAsync();
        var reopened = Assert.Single(provisionals);
        Assert.Equal(FinancialDocumentStatus.Settled, reopened.Status);
        Assert.Equal(FirmPayableSeed.DefaultFirmAmount, reopened.NetAmount);
        Assert.Equal(ContractStatus.Approved,
            (await seed.Context.PurchaseContracts.AsNoTracking().SingleAsync(x => x.Key == seed.Contract.Key)).Status);
    }

    [Fact]
    public async Task Finished_contract_guard_does_not_block_financial_changes()
    {
        var db = TestDb.CreateUnitOfWork(new FinishedContractMutationGuardInterceptor());
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(), Code = "PC-9", CardCode = "F0001", ItemCode = "SOJA", UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "25/26", DeliveryLocationCode = "01", TotalVolume = 1_000m, Status = ContractStatus.Finished,
        };
        var provisional = new FinancialDocument
        {
            Key = Guid.NewGuid(), Code = "FN-9", CardCode = "F0001", Direction = FinancialDirection.Payable,
            Nature = FinancialDocumentNature.Provisional, NetAmount = 1_000m, DueDate = new DateTime(2026, 12, 31),
            PurchaseContractKey = contract.Key,
        };
        db.Context.PurchaseContracts.Add(contract);
        db.Context.FinancialDocuments.Add(provisional);
        await db.Context.SaveChangesAsync();

        db.Context.FinancialDocumentCompositions.Add(new FinancialDocumentComposition
        {
            Key = Guid.NewGuid(), ProvisionalDocumentKey = provisional.Key, ComposedDocumentKey = Guid.NewGuid(), Amount = 10m,
        });
        provisional.NetAmount = 900m;
        await db.Context.SaveChangesAsync(); // regra 30: o interceptor não ganha FinancialDocument nem a composição

        Assert.Equal(900m, (await db.Context.FinancialDocuments.AsNoTracking().SingleAsync()).NetAmount);
    }
}
