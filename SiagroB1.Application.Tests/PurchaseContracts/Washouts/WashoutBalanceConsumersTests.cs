using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseContracts.Washouts;

public class WashoutBalanceConsumersTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private PurchaseContractsCloseService CloseService() =>
        new(_db.Context, new PurchaseContractsFixedVolumeService(_db.Context), TestNotificationOutbox.For(_db.Context),
            FinancialDocumentTestServices.Cancel(_db.Context));

    private PurchaseContractsCancelService CancelService() =>
        new(_db, TestNotificationOutbox.For(_db.Context),
            FinancialDocumentTestServices.Cancel(_db.Context), FinancialDocumentTestServices.CancellationGuard(_db.Context));

    private async Task<PurchaseContract> SeedAsync(PurchaseContract contract, params object[] children)
    {
        _db.Context.PurchaseContracts.Add(contract);
        foreach (var child in children) _db.Context.Add(child);
        await _db.Context.SaveChangesAsync();
        return contract;
    }

    [Fact]
    public async Task Fully_washed_contract_leaves_the_release_selector()
    {
        var contract = WashoutTestData.Contract(totalVolume: 1_000m);
        contract.WashedOutVolume = 1_000m;
        await SeedAsync(contract);

        var service = new PurchaseContractsGetShipmentReleasesAvailableService(
            _db, NullLogger<PurchaseContractsGetShipmentReleasesAvailableService>.Instance);

        Assert.DoesNotContain(await service.Query().ToListAsync(), c => c.Key == contract.Key);
    }

    [Fact]
    public async Task Close_refuses_a_pending_washout()
    {
        var contract = WashoutTestData.Contract(totalVolume: 1_000m);
        await SeedAsync(contract, WashoutTestData.Washout(contract, unfixedVolume: 100m));

        var error = await Assert.ThrowsAsync<ApplicationException>(() => CloseService().ExecuteAsync(contract.Key, "tester"));

        Assert.Contains("washout", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Close_counts_approved_washouts_in_the_negative_balance_guard()
    {
        var contract = WashoutTestData.Contract(totalVolume: 1_000m);
        await SeedAsync(contract, WashoutTestData.Washout(contract, unfixedVolume: 1_500m,
            status: PurchaseContractWashoutStatus.Approved));

        var error = await Assert.ThrowsAsync<ApplicationException>(() => CloseService().ExecuteAsync(contract.Key, "tester"));

        Assert.Contains("washout:", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Close_with_an_approved_washout_inside_the_balance_succeeds()
    {
        var contract = WashoutTestData.Contract(totalVolume: 1_000m);
        await SeedAsync(contract, WashoutTestData.Washout(contract, unfixedVolume: 400m,
            status: PurchaseContractWashoutStatus.Approved));

        await CloseService().ExecuteAsync(contract.Key, "tester");

        Assert.Equal(ContractStatus.Finished,
            (await _db.Context.PurchaseContracts.AsNoTracking().SingleAsync(x => x.Key == contract.Key)).Status);
    }

    /// <summary>
    /// F2 (revisão final): confirmado 30.000, entregue 20.000, washout Approved lavou 15.000
    /// fixados. 30.000 − 15.000 = 15.000 &lt; 20.000: a guarda de encerramento PAF precisa
    /// descontar o lavado do confirmado, senão fecha o contrato devendo preço de mercadoria já
    /// entregue.
    /// </summary>
    [Fact]
    public async Task Close_Paf_discounts_the_approved_washed_fixed_volume_from_the_price_fixation_guard()
    {
        var contract = WashoutTestData.Contract(ContractType.ToBeDetermined, totalVolume: 100_000m);
        var fixation = WashoutTestData.Fixation(contract, 30_000m);
        var release = new ShipmentRelease
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = contract.Key,
            DeliveryLocationCode = "01",
            ReleasedQuantity = 20_000m,
            ShippedQuantity = 20_000m,
            Status = ReleaseStatus.Actived,
        };
        var washout = WashoutTestData.Washout(contract, fixation, fixedVolume: 15_000m,
            status: PurchaseContractWashoutStatus.Approved);
        await SeedAsync(contract, fixation, release, washout);

        var error = await Assert.ThrowsAsync<ApplicationException>(() => CloseService().ExecuteAsync(contract.Key, "tester"));

        Assert.Contains("Volume entregue sem preço fixado", error.Message);
    }

    /// <summary>
    /// F6 (revisão final): washout InApproval reserva volume mas ainda pode ser rejeitado.
    /// Cancelar o contrato com ele pendente órfão o washout, sem contrato ativo pra decidir.
    /// </summary>
    [Fact]
    public async Task Cancel_refuses_a_contract_with_a_pending_washout()
    {
        var contract = WashoutTestData.Contract(totalVolume: 1_000m);
        await SeedAsync(contract, WashoutTestData.Washout(contract, unfixedVolume: 100m));

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => CancelService().ExecuteAsync(contract.Key, null, "tester"));

        Assert.Contains("washout", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ContractStatus.Approved,
            (await _db.Context.PurchaseContracts.AsNoTracking().SingleAsync(x => x.Key == contract.Key)).Status);
    }

    [Fact]
    public async Task Price_fixation_cannot_use_volume_already_washed_unfixed()
    {
        var contract = WashoutTestData.Contract(ContractType.ToBeDetermined);
        contract.WashedOutUnfixedVolume = 60_000m;
        await SeedAsync(contract);

        var service = new PurchaseContractsPriceFixationCreateService(
            _db.Context, new PurchaseContractsFixedVolumeService(_db.Context),
            new PurchaseContractsChangeLogService(_db.Context), TestNotificationOutbox.For(_db.Context),
            NullLogger<PurchaseContractsPriceFixationCreateService>.Instance);

        await Assert.ThrowsAsync<ApplicationException>(() => service.ExecuteAsync(contract.Key,
            new PurchaseContractPriceFixation { FixationVolume = 50_000m, FixationPrice = 2.5m }, "tester"));
    }

    [Fact]
    public async Task Price_fixation_reversal_refuses_a_fixation_with_an_active_washout()
    {
        var contract = WashoutTestData.Contract(ContractType.ToBeDetermined);
        var fixation = WashoutTestData.Fixation(contract, 30_000m);
        await SeedAsync(contract, fixation,
            WashoutTestData.Washout(contract, fixation, fixedVolume: 1_000m, status: PurchaseContractWashoutStatus.Approved));

        var service = new PurchaseContractsPriceFixationsCancelService(
            _db.Context, new PurchaseContractsFixedVolumeService(_db.Context),
            new PurchaseContractsChangeLogService(_db.Context), TestNotificationOutbox.For(_db.Context),
            FinancialDocumentTestServices.Cancel(_db.Context));

        var error = await Assert.ThrowsAsync<ApplicationException>(() => service.ExecuteAsync(fixation.Key, "tester"));

        Assert.Contains("washout", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Withdraw_approval_refuses_a_contract_with_any_washout()
    {
        var contract = WashoutTestData.Contract();
        await SeedAsync(contract, WashoutTestData.Washout(contract, unfixedVolume: 10m,
            status: PurchaseContractWashoutStatus.Rejected));

        var service = new PurchaseContractsWithdrawApprovalService(
            _db, new FakeStringLocalizer<Resource>(), TestNotificationOutbox.For(_db.Context));

        await Assert.ThrowsAsync<BusinessException>(() => service.ExecuteAsync(contract.Key, "tester"));
    }

    [Fact]
    public async Task Totals_expose_the_washed_volume_and_discount_it_from_the_balance()
    {
        var contract = WashoutTestData.Contract(totalVolume: 1_000m);
        contract.WashedOutVolume = 300m;
        await SeedAsync(contract);

        var totals = await new PurchaseContractsTotalsService(_db.Context).GetTotals(contract.Key);

        Assert.Equal(300m, totals.WashedOutVolume);
        Assert.Equal(700m, totals.AvaiableVolume);
    }
}
