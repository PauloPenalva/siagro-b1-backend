using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseContracts.Washouts;

public class PurchaseContractsWashoutReverseServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private PurchaseContractsWashoutReverseService Service() => new(
        _db.Context,
        new PurchaseContractsWashedOutVolumeService(_db.Context),
        new PurchaseContractsChangeLogService(_db.Context),
        TestNotificationOutbox.For(_db.Context),
        new FinancialDocumentsAdjustProvisionalService(
            _db.Context, FinancialDocumentTestServices.Generate(_db.Context), new FinancialDocumentChangeLogService(_db.Context)),
        FinancialDocumentTestServices.Cancel(_db.Context));

    /// <summary>
    /// Estado de um washout JÁ APROVADO sobre contrato de preço fixo 100.000 @ 2,50: provisório
    /// reduzido (ou cancelado, quando lavou tudo) e título a receber aberto.
    /// </summary>
    private async Task<(PurchaseContract Contract, PurchaseContractWashout Washout, FinancialDocument Receivable)> SeedApprovedAsync(
        decimal fixedVolume = 10_000m, ContractStatus contractStatus = ContractStatus.Approved)
    {
        var contract = WashoutTestData.Contract(status: contractStatus);
        contract.FixedVolume = 100_000m;
        contract.WashedOutVolume = fixedVolume;
        var fixation = WashoutTestData.Fixation(contract, 100_000m);
        var washout = WashoutTestData.Washout(contract, fixation, fixedVolume: fixedVolume,
            status: PurchaseContractWashoutStatus.Approved);

        var provisional = WashoutTestData.Provisional(contract, fixation);
        provisional.NetAmount = decimal.Round((100_000m - fixedVolume) * 2.5m, 2);
        if (fixedVolume >= 100_000m) provisional.Status = FinancialDocumentStatus.Canceled;

        var receivable = new FinancialDocument
        {
            Key = Guid.NewGuid(),
            Code = "FD-0002",
            CardCode = contract.CardCode,
            Direction = FinancialDirection.Receivable,
            Nature = FinancialDocumentNature.Firm,
            Status = FinancialDocumentStatus.Open,
            DueDate = new DateTime(2026, 10, 31),
            NetAmount = washout.Amount,
            OriginType = FinancialDocumentOrigin.PurchaseContractWashout,
            OriginKey = washout.Key,
            OriginDocNumber = contract.Code,
            PurchaseContractKey = contract.Key,
        };
        washout.FinancialDocumentKey = receivable.Key;

        _db.Context.PurchaseContracts.Add(contract);
        _db.Context.PurchaseContractsPriceFixations.Add(fixation);
        _db.Context.FinancialDocuments.AddRange(provisional, receivable);
        _db.Context.PurchaseContractsWashouts.Add(washout);
        await _db.Context.SaveChangesAsync();
        return (contract, washout, receivable);
    }

    private IQueryable<FinancialDocument> OpenPayables() => _db.Context.FinancialDocuments.AsNoTracking()
        .Where(x => x.Direction == FinancialDirection.Payable && x.Status != FinancialDocumentStatus.Canceled);

    private async Task<PurchaseContractWashout> ReloadAsync(Guid key) =>
        await _db.Context.PurchaseContractsWashouts.AsNoTracking().SingleAsync(x => x.Key == key);

    [Fact]
    public async Task Reverse_marks_reversed_and_records_the_reason()
    {
        var (_, washout, _) = await SeedApprovedAsync();

        await Service().ExecuteAsync(washout.Key, "lançado errado", "gerente");

        var reloaded = await ReloadAsync(washout.Key);
        Assert.Equal(PurchaseContractWashoutStatus.Reversed, reloaded.Status);
        Assert.Equal("lançado errado", reloaded.ReversalReason);
        Assert.Equal("gerente", reloaded.CanceledBy);
        Assert.Contains(_db.Context.NotificationOutboxMessages, m => m.EventType == NotificationEventType.WashoutReversed);
    }

    [Fact]
    public async Task Reverse_cancels_the_receivable()
    {
        var (_, washout, receivable) = await SeedApprovedAsync();

        await Service().ExecuteAsync(washout.Key, "lançado errado", "gerente");

        var reloaded = await _db.Context.FinancialDocuments.AsNoTracking().SingleAsync(x => x.Key == receivable.Key);
        Assert.Equal(FinancialDocumentStatus.Canceled, reloaded.Status);
        Assert.Equal(PurchaseContractsWashoutReverseService.ReceivableCancellationReason, reloaded.CancellationReason);
    }

    [Fact]
    public async Task Reverse_restores_the_provisional_amount()
    {
        var (_, washout, _) = await SeedApprovedAsync();

        await Service().ExecuteAsync(washout.Key, "lançado errado", "gerente");

        Assert.Equal(250_000m, Assert.Single(OpenPayables()).NetAmount);
    }

    [Fact]
    public async Task Reverse_regenerates_the_provisional_canceled_by_a_full_washout()
    {
        var (_, washout, _) = await SeedApprovedAsync(fixedVolume: 100_000m);

        await Service().ExecuteAsync(washout.Key, "lançado errado", "gerente");

        Assert.Equal(250_000m, Assert.Single(OpenPayables()).NetAmount);
    }

    [Fact]
    public async Task Reverse_releases_the_washed_volume()
    {
        var (contract, washout, _) = await SeedApprovedAsync();

        await Service().ExecuteAsync(washout.Key, "lançado errado", "gerente");

        Assert.Equal(0m, (await _db.Context.PurchaseContracts.AsNoTracking().SingleAsync(x => x.Key == contract.Key)).WashedOutVolume);
    }

    [Fact]
    public async Task Reverse_refuses_a_receivable_with_settlements()
    {
        var (_, washout, receivable) = await SeedApprovedAsync();
        var tracked = await _db.Context.FinancialDocuments.SingleAsync(x => x.Key == receivable.Key);
        tracked.SettledAmount = 3_500m;
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(washout.Key, "lançado errado", "gerente"));

        Assert.Contains("baixa", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(PurchaseContractWashoutStatus.Approved, (await ReloadAsync(washout.Key)).Status);
    }

    [Fact]
    public async Task Reverse_refuses_a_finished_contract()
    {
        var (_, washout, _) = await SeedApprovedAsync(contractStatus: ContractStatus.Finished);

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(washout.Key, "lançado errado", "gerente"));

        Assert.Contains("Reabra", error.Message);
    }

    /// <summary>
    /// F6 (revisão final): "Reabra o contrato antes" só faz sentido para Finished — reabrir é a
    /// ação que devolve o contrato para Approved. Um contrato Canceled não reabre.
    /// </summary>
    [Fact]
    public async Task Reverse_refuses_a_canceled_contract_without_mentioning_reopen()
    {
        var (_, washout, _) = await SeedApprovedAsync(contractStatus: ContractStatus.Canceled);

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(washout.Key, "lançado errado", "gerente"));

        Assert.DoesNotContain("Reabra", error.Message);
    }

    [Fact]
    public async Task Reverse_requires_a_reason()
    {
        var (_, washout, _) = await SeedApprovedAsync();

        await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(washout.Key, " ", "gerente"));
    }

    [Fact]
    public async Task Reverse_refuses_a_reason_longer_than_500_characters()
    {
        var (_, washout, _) = await SeedApprovedAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(washout.Key, new string('a', 501), "gerente"));

        Assert.Contains("500 caracteres", error.Message);
    }

    /// <summary>
    /// F7 (revisão final): §8 do spec pedia o estorno SEM título (<c>Amount == 0</c>, sem
    /// <c>FinancialDocumentKey</c>) e não havia teste. O estorno só chama
    /// <c>EnqueueCancelByKeyAsync</c> dentro de <c>if (washout.FinancialDocumentKey is { } receivableKey)</c>
    /// — sem este teste, quebrar essa condição passaria em silêncio.
    /// </summary>
    [Fact]
    public async Task Reverse_succeeds_for_an_approved_washout_without_a_receivable()
    {
        var contract = WashoutTestData.Contract();
        contract.FixedVolume = 100_000m;
        contract.WashedOutVolume = 10_000m;
        var fixation = WashoutTestData.Fixation(contract, 100_000m);
        var washout = WashoutTestData.Washout(contract, fixation, fixedVolume: 10_000m,
            status: PurchaseContractWashoutStatus.Approved, marketPrice: 2.5m, penaltyAmount: 0m);
        var provisional = WashoutTestData.Provisional(contract, fixation);
        provisional.NetAmount = decimal.Round((100_000m - 10_000m) * 2.5m, 2);

        _db.Context.PurchaseContracts.Add(contract);
        _db.Context.PurchaseContractsPriceFixations.Add(fixation);
        _db.Context.FinancialDocuments.Add(provisional);
        _db.Context.PurchaseContractsWashouts.Add(washout);
        await _db.Context.SaveChangesAsync();

        Assert.Equal(0m, washout.Amount);
        Assert.Null(washout.FinancialDocumentKey);

        await Service().ExecuteAsync(washout.Key, "lançado errado", "gerente");

        var reloaded = await ReloadAsync(washout.Key);
        Assert.Equal(PurchaseContractWashoutStatus.Reversed, reloaded.Status);
        Assert.Equal(250_000m, Assert.Single(OpenPayables()).NetAmount);
        Assert.Equal(0m, (await _db.Context.PurchaseContracts.AsNoTracking()
            .SingleAsync(x => x.Key == contract.Key)).WashedOutVolume);
    }

    [Fact]
    public async Task Reverse_refuses_a_washout_that_is_not_approved()
    {
        var (_, washout, _) = await SeedApprovedAsync();
        var tracked = await _db.Context.PurchaseContractsWashouts.SingleAsync(x => x.Key == washout.Key);
        tracked.Status = PurchaseContractWashoutStatus.InApproval;
        await _db.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(washout.Key, "lançado errado", "gerente"));
    }
}
