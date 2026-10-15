using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesContracts.Washouts;

/// <summary>Spec 2026-10-15 (washout de venda) §5.6 e decisão 5 do plano: travas em serviços existentes.</summary>
public class SalesWashoutContractGuardsTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private SalesContractsCloseService Close() =>
        new(_db.Context, new SalesContractsFixedVolumeService(_db.Context), TestNotificationOutbox.For(_db.Context),
            TestConfiguration.Empty(), NullLogger<SalesContractsCloseService>.Instance);

    private async Task SeedAsync(SalesContract contract, params object[] children)
    {
        _db.Context.SalesContracts.Add(contract);
        foreach (var child in children) _db.Context.Add(child);
        await _db.Context.SaveChangesAsync();
    }

    [Fact]
    public async Task Close_refuses_a_pending_washout()
    {
        var contract = SalesWashoutTestData.Contract(ContractType.ToBeDetermined);
        await SeedAsync(contract, SalesWashoutTestData.Washout(contract, unfixedVolume: 100m));

        var error = await Assert.ThrowsAsync<ApplicationException>(() => Close().ExecuteAsync(contract.Key, "tester"));

        Assert.Equal("Contrato possui 1 washout(s) pendente(s) de aprovação. Aprove ou rejeite antes de encerrar.", error.Message);
    }

    [Fact]
    public async Task Close_counts_approved_washouts_in_the_negative_balance_guard()
    {
        var contract = SalesWashoutTestData.Contract();
        var (invoice, allocation) = SalesWashoutTestData.Billed(contract, 900m);
        await SeedAsync(contract, invoice,
            SalesWashoutTestData.Washout(contract, unfixedVolume: 200m, status: SalesContractWashoutStatus.Approved));
        _db.Context.SalesContractsAllocations.Add(allocation);
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(() => Close().ExecuteAsync(contract.Key, "tester"));

        Assert.Contains("Contrato faturado além do volume contratado", error.Message);
        Assert.Contains("washout", error.Message);
    }

    [Fact]
    public async Task Close_of_paf_discounts_the_approved_washed_fixed_volume()
    {
        // 500 fixados e confirmados, 400 lavados (aprovados), 300 faturados: sobram 100 com preço para 300 entregues.
        var contract = SalesWashoutTestData.Contract(ContractType.ToBeDetermined);
        var fixation = SalesWashoutTestData.Fixation(contract, 500m);
        await SeedAsync(contract, fixation,
            SalesWashoutTestData.Washout(contract, fixation, fixedVolume: 400m, status: SalesContractWashoutStatus.Approved),
            SalesContractsAllocationTestSupport.NewRelease(contract.Key, released: 300m, shipped: 300m));

        var error = await Assert.ThrowsAsync<ApplicationException>(() => Close().ExecuteAsync(contract.Key, "tester"));

        Assert.Contains("Volume entregue sem preço fixado", error.Message);
        Assert.Contains("descontado o washout", error.Message);
    }

    [Fact]
    public async Task Reversing_a_fixation_with_an_active_washout_is_refused()
    {
        var contract = SalesWashoutTestData.Contract();
        var fixation = SalesWashoutTestData.Fixation(contract, 1_000m);
        await SeedAsync(contract, fixation,
            SalesWashoutTestData.Washout(contract, fixation, fixedVolume: 100m, status: SalesContractWashoutStatus.Approved));

        var service = new SalesContractsPriceFixationsCancelService(
            _db.Context, new SalesContractsFixedVolumeService(_db.Context), new SalesContractsChangeLogService(_db.Context),
            TestNotificationOutbox.For(_db.Context), FinancialDocumentTestServices.Cancel(_db.Context));

        var error = await Assert.ThrowsAsync<ApplicationException>(() => service.ExecuteAsync(fixation.Key, "tester"));

        Assert.Equal("Fixação possui washout registrado. Rejeite ou estorne o washout antes de estornar a fixação.", error.Message);
    }

    [Fact]
    public async Task Reversing_a_fixation_after_the_washout_was_reversed_is_allowed()
    {
        var contract = SalesWashoutTestData.Contract();
        var fixation = SalesWashoutTestData.Fixation(contract, 1_000m);
        await SeedAsync(contract, fixation,
            SalesWashoutTestData.Washout(contract, fixation, fixedVolume: 100m, status: SalesContractWashoutStatus.Reversed));

        await new SalesContractsPriceFixationsCancelService(
                _db.Context, new SalesContractsFixedVolumeService(_db.Context), new SalesContractsChangeLogService(_db.Context),
                TestNotificationOutbox.For(_db.Context), FinancialDocumentTestServices.Cancel(_db.Context))
            .ExecuteAsync(fixation.Key, "tester");

        Assert.Equal(PriceFixationStatus.InApproval,
            (await _db.Context.SalesContractsPriceFixations.AsNoTracking().SingleAsync(f => f.Key == fixation.Key)).Status);
    }

    [Fact]
    public async Task Deleting_a_fixation_with_any_washout_is_refused()
    {
        var contract = SalesWashoutTestData.Contract(ContractType.ToBeDetermined);
        var fixation = SalesWashoutTestData.Fixation(contract, 300m, status: PriceFixationStatus.InApproval);
        await SeedAsync(contract, fixation,
            SalesWashoutTestData.Washout(contract, fixation, fixedVolume: 100m, status: SalesContractWashoutStatus.Rejected));

        var service = new SalesContractsPriceFixationDeleteService(
            _db.Context, new SalesContractsFixedVolumeService(_db.Context), new SalesContractsChangeLogService(_db.Context),
            NullLogger<SalesContractsPriceFixationDeleteService>.Instance);

        var error = await Assert.ThrowsAsync<ApplicationException>(() => service.ExecuteAsync(fixation.Key, "tester"));

        Assert.StartsWith("Fixação possui washout registrado (mesmo rejeitado ou estornado)", error.Message);
    }

    [Fact]
    public async Task Withdrawing_approval_with_any_washout_is_refused()
    {
        var contract = SalesWashoutTestData.Contract();
        await SeedAsync(contract,
            SalesWashoutTestData.Washout(contract, unfixedVolume: 1m, status: SalesContractWashoutStatus.Rejected));

        var service = new SalesContractsWithdrawApprovalService(_db, new FakeStringLocalizer<Resource>(),
            TestNotificationOutbox.For(_db.Context), FinancialDocumentTestServices.Cancel(_db.Context));

        var error = await Assert.ThrowsAsync<BusinessException>(() => service.ExecuteAsync(contract.Key, "tester"));

        Assert.Equal("SALES_CONTRACT_HAS_WASHOUTS", error.Message);
    }

    [Fact]
    public async Task Cancelling_the_contract_with_a_pending_washout_is_refused()
    {
        var contract = SalesWashoutTestData.Contract(ContractType.ToBeDetermined);
        await SeedAsync(contract, SalesWashoutTestData.Washout(contract, unfixedVolume: 100m));

        var service = new SalesContractsCancelService(_db, TestNotificationOutbox.For(_db.Context),
            FinancialDocumentTestServices.Cancel(_db.Context), FinancialDocumentTestServices.CancellationGuard(_db.Context));

        var error = await Assert.ThrowsAsync<ApplicationException>(() => service.ExecuteAsync(contract.Key, null, "tester"));

        Assert.Equal("Contrato possui 1 washout(s) pendente(s) de aprovação. Aprove ou rejeite antes de cancelar.", error.Message);
    }
}
