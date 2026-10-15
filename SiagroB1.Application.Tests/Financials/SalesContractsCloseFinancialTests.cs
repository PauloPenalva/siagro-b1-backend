using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>Spec 2026-10-15, regras 15–18: encerrar contrato de venda só com tudo quitado; corte legado; estorno de fixação.</summary>
public class SalesContractsCloseFinancialTests
{
    private static SalesContractsCloseService Close(SalesFirmReceivableSeed seed, string? cutover = null) =>
        new(seed.Context, new SalesContractsFixedVolumeService(seed.Context), TestNotificationOutbox.For(seed.Context),
            TestConfiguration.With((SalesContractsCloseService.CutoverDateKey, cutover)),
            NullLogger<SalesContractsCloseService>.Instance);

    [Fact]
    public async Task Open_firm_blocks_the_close()
    {
        var seed = await SalesFirmReceivableSeed.CreateAsync();
        await seed.ConfirmedAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(() => Close(seed).ExecuteAsync(seed.Contract.Key, "ana"));

        Assert.Equal("Há títulos firmes com saldo neste contrato: componha, compense ou baixe por dação antes de encerrar.", error.Message);
    }

    [Fact]
    public async Task Unsettled_provisional_blocks_the_close()
    {
        var seed = await SalesFirmReceivableSeed.CreateAsync(provisionalAmount: 90_000m);

        var error = await Assert.ThrowsAsync<ApplicationException>(() => Close(seed).ExecuteAsync(seed.Contract.Key, "ana"));

        Assert.Equal("O título FN-PROV-V1 não está quitado: o contrato só encerra com todos os títulos quitados.", error.Message);
    }

    [Fact]
    public async Task Fully_received_contract_closes_and_keeps_the_provisional()
    {
        var seed = await SalesFirmReceivableSeed.CreateAsync(provisionalAmount: 90_000m);
        var (_, line) = await seed.ConfirmedAsync();
        await seed.ComposeAsync(Assert.Single(await seed.TitlesOfAsync(line)));
        await new FinancialDocumentsSettleService(seed.Db)
            .ExecuteAsync(seed.Provisional.Key, "CX01", 90_000m, DateTime.Today, 0m, 0m, 0m, null, null, "ana");

        await Close(seed).ExecuteAsync(seed.Contract.Key, "ana");

        Assert.Equal(ContractStatus.Finished,
            (await seed.Context.SalesContracts.AsNoTracking().SingleAsync(c => c.Key == seed.Contract.Key)).Status);
        Assert.Equal(FinancialDocumentStatus.Settled, (await seed.ReloadAsync(seed.Provisional.Key)).Status);
    }

    [Fact]
    public async Task Untouched_provisional_before_the_cutover_is_cancelled_on_close()
    {
        var seed = await SalesFirmReceivableSeed.CreateAsync();
        var legacy = await seed.Context.FinancialDocuments.SingleAsync(d => d.Key == seed.Provisional.Key);
        legacy.CreatedAt = new DateTime(2026, 9, 1);
        await seed.Context.SaveChangesAsync();

        await Close(seed, cutover: "2026-10-15").ExecuteAsync(seed.Contract.Key, "ana");

        var cancelled = await seed.ReloadAsync(seed.Provisional.Key);
        Assert.Equal(FinancialDocumentStatus.Canceled, cancelled.Status);
        Assert.Equal("Encerramento do contrato", cancelled.CancellationReason);
    }

    [Fact]
    public async Task Without_the_cutover_key_nothing_is_exempt()
    {
        var seed = await SalesFirmReceivableSeed.CreateAsync();
        var legacy = await seed.Context.FinancialDocuments.SingleAsync(d => d.Key == seed.Provisional.Key);
        legacy.CreatedAt = new DateTime(2026, 9, 1);
        await seed.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<ApplicationException>(() => Close(seed).ExecuteAsync(seed.Contract.Key, "ana"));
    }

    [Fact]
    public async Task Advance_with_credit_blocks_the_close()
    {
        var seed = await SalesFirmReceivableSeed.CreateAsync();
        var tracked = await seed.Context.FinancialDocuments.SingleAsync(d => d.Key == seed.Provisional.Key);
        tracked.CreatedAt = new DateTime(2026, 9, 1);
        seed.Context.FinancialDocuments.Add(new FinancialDocument
        {
            Key = Guid.NewGuid(), Code = "FN-ADV-V1", CardCode = "C0001", Direction = FinancialDirection.Receivable,
            Nature = FinancialDocumentNature.Advance, Status = FinancialDocumentStatus.Settled, NetAmount = 1_000m,
            SettledAmount = 1_000m, AppliedAmount = 0m, DueDate = new DateTime(2026, 12, 31),
            OriginType = FinancialDocumentOrigin.Manual, SalesContractKey = seed.Contract.Key, Currency = CurrencyType.Brl,
        });
        await seed.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(() =>
            Close(seed, cutover: "2026-10-15").ExecuteAsync(seed.Contract.Key, "ana"));

        Assert.Equal(
            "O adiantamento FN-ADV-V1 tem 1.000,00 de crédito não aplicado: compense, devolva ou vincule a outro contrato antes de encerrar.",
            error.Message);
    }

    [Fact]
    public async Task Reversing_a_fixation_whose_provisional_is_composed_is_refused()
    {
        var seed = await SalesFirmReceivableSeed.CreateAsync();
        var fixation = new SalesContractPriceFixation
        {
            Key = Guid.NewGuid(), SalesContractKey = seed.Contract.Key, FixationVolume = 1_000m, FixationPrice = 100m,
            Status = PriceFixationStatus.Confirmed, ApprovedBy = "diretoria", ApprovedAt = DateTime.Now,
        };
        seed.Context.SalesContractsPriceFixations.Add(fixation);
        var provisional = await seed.Context.FinancialDocuments.SingleAsync(d => d.Key == seed.Provisional.Key);
        provisional.OriginKey = fixation.Key;
        provisional.ComposedAmount = 100m;
        await seed.Context.SaveChangesAsync();

        var service = new SalesContractsPriceFixationsCancelService(
            seed.Context, new SalesContractsFixedVolumeService(seed.Context), new SalesContractsChangeLogService(seed.Context),
            TestNotificationOutbox.For(seed.Context), FinancialDocumentTestServices.Cancel(seed.Context));

        var error = await Assert.ThrowsAsync<ApplicationException>(() => service.ExecuteAsync(fixation.Key, "ana"));

        Assert.Equal($"Desfaça a composição do título {provisional.Code} antes de estornar a fixação.", error.Message);
    }
}
