using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Interfaces;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseContracts;

/// <summary>
/// Situação financeira da liberação de pagamento (GAC-1188): vale em qualquer status do contrato,
/// exige a permissão do Financeiro e respeita a regra de contrato sobre rodas.
/// </summary>
public class PurchaseContractPaymentReleaseStatusTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private PurchaseContractsSetPaymentReleaseStatusService Service(bool allowed = true) => new(
        _db.Context,
        new PurchaseContractsChangeLogService(_db.Context),
        allowed ? new FakeUserPermissions(PermissionCodes.PaymentReleaseFinancial) : new FakeUserPermissions());

    private async Task<PurchaseContract> SeedAsync(
        ContractStatus status = ContractStatus.Approved,
        PaymentReleaseStatus? release = null,
        bool onWheels = false)
    {
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(),
            Code = "PC-001",
            CardCode = "F0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            TotalVolume = 100_000m,
            HarvestSeasonCode = "24/25",
            DeliveryLocationCode = "01",
            StandardPrice = 2m,
            Type = ContractType.Fixed,
            Status = status,
            PaymentReleaseStatus = release,
            IsOnWheels = onWheels,
        };

        _db.Context.PurchaseContracts.Add(contract);
        await _db.Context.SaveChangesAsync();

        return contract;
    }

    private Task<List<PurchaseContractChangeLog>> LogsAsync(Guid key) =>
        _db.Context.PurchaseContractsChangeLogs.Where(l => l.PurchaseContractKey == key).ToListAsync();

    private Task<PurchaseContract> ReloadAsync(Guid key) =>
        _db.Context.PurchaseContracts.AsNoTracking().FirstAsync(c => c.Key == key);

    [Fact]
    public async Task SetStatus_WithoutPermission_Throws_AndKeepsValue()
    {
        var contract = await SeedAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            Service(allowed: false).SetStatusAsync(contract.Key, PaymentReleaseStatus.UnderReview, "joao"));

        Assert.Equal("Você não tem permissão para alterar a liberação financeira do contrato.", ex.Message);
        Assert.Null((await ReloadAsync(contract.Key)).PaymentReleaseStatus);
        Assert.Empty(await LogsAsync(contract.Key));
    }

    [Fact]
    public async Task EarlyPayment_WithoutPermission_Throws()
    {
        var contract = await SeedAsync();

        await Assert.ThrowsAsync<DefaultException>(() =>
            Service(allowed: false).SetEarlyPaymentAuthorizedAsync(contract.Key, true, "joao"));

        Assert.False((await ReloadAsync(contract.Key)).EarlyPaymentAuthorized);
    }

    [Fact]
    public async Task SetStatus_MissingContract_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            Service().SetStatusAsync(Guid.NewGuid(), PaymentReleaseStatus.UnderReview, "joao"));
    }

    [Theory]
    [InlineData(ContractStatus.Finished)]
    [InlineData(ContractStatus.Canceled)]
    [InlineData(ContractStatus.Approved)]
    [InlineData(ContractStatus.Draft)]
    public async Task SetStatus_AnyContractStatus_Saves(ContractStatus status)
    {
        var contract = await SeedAsync(status);

        await Service().SetStatusAsync(contract.Key, PaymentReleaseStatus.Approved, "joao");

        var reloaded = await ReloadAsync(contract.Key);
        Assert.Equal(PaymentReleaseStatus.Approved, reloaded.PaymentReleaseStatus);
        Assert.Equal(status, reloaded.Status);
        Assert.Equal("joao", reloaded.UpdatedBy);
    }

    [Fact]
    public async Task SetStatus_LogsPtBrLabels()
    {
        var contract = await SeedAsync();

        await Service().SetStatusAsync(contract.Key, PaymentReleaseStatus.UnderReview, "maria");

        var log = Assert.Single(await LogsAsync(contract.Key));
        Assert.Equal(ContractChangeLogFields.PaymentReleaseStatus, log.Field);
        Assert.Equal("Pendente", log.OldValue);
        Assert.Equal("Em análise", log.NewValue);
        Assert.Equal("maria", log.ChangedBy);
    }

    [Fact]
    public async Task SetStatus_Same_IsNoOp()
    {
        var contract = await SeedAsync(release: PaymentReleaseStatus.UnderReview);

        await Service().SetStatusAsync(contract.Key, PaymentReleaseStatus.UnderReview, "joao");

        Assert.Empty(await LogsAsync(contract.Key));
        Assert.NotEqual("joao", (await ReloadAsync(contract.Key)).UpdatedBy);
    }

    [Fact]
    public async Task PreApproved_NotOnWheels_Throws()
    {
        var contract = await SeedAsync(onWheels: false);

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            Service().SetStatusAsync(contract.Key, PaymentReleaseStatus.PreApproved, "joao"));

        Assert.Equal("Pré-aprovado só se aplica a contrato sobre rodas.", ex.Message);
        Assert.Null((await ReloadAsync(contract.Key)).PaymentReleaseStatus);
    }

    [Fact]
    public async Task Approved_OnWheels_Throws()
    {
        var contract = await SeedAsync(onWheels: true);

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            Service().SetStatusAsync(contract.Key, PaymentReleaseStatus.Approved, "joao"));

        Assert.Equal("Contrato sobre rodas deve ser marcado como Pré-aprovado.", ex.Message);
    }

    [Fact]
    public async Task PreApproved_OnWheels_Saves()
    {
        var contract = await SeedAsync(onWheels: true);

        await Service().SetStatusAsync(contract.Key, PaymentReleaseStatus.PreApproved, "joao");

        Assert.Equal(PaymentReleaseStatus.PreApproved, (await ReloadAsync(contract.Key)).PaymentReleaseStatus);
    }

    /// <summary>
    /// A regra vale só no momento da mudança: desmarcar "sobre rodas" depois não reverte o status
    /// já gravado, mas a próxima mudança de status volta a seguir a regra.
    /// </summary>
    [Fact]
    public async Task OnWheelsChangedAfterPreApproved_KeepsStatus_NextChangeFollowsRule()
    {
        var contract = await SeedAsync(onWheels: true);
        await Service().SetStatusAsync(contract.Key, PaymentReleaseStatus.PreApproved, "joao");

        var tracked = await _db.Context.PurchaseContracts.FirstAsync(c => c.Key == contract.Key);
        tracked.IsOnWheels = false;
        await _db.Context.SaveChangesAsync();

        Assert.Equal(PaymentReleaseStatus.PreApproved, (await ReloadAsync(contract.Key)).PaymentReleaseStatus);

        await Service().SetStatusAsync(contract.Key, PaymentReleaseStatus.UnderReview, "joao");
        await Assert.ThrowsAsync<DefaultException>(() =>
            Service().SetStatusAsync(contract.Key, PaymentReleaseStatus.PreApproved, "joao"));
        await Service().SetStatusAsync(contract.Key, PaymentReleaseStatus.Approved, "joao");

        Assert.Equal(PaymentReleaseStatus.Approved, (await ReloadAsync(contract.Key)).PaymentReleaseStatus);
    }

    [Fact]
    public async Task EarlyPayment_Toggle_Logs()
    {
        var contract = await SeedAsync();

        await Service().SetEarlyPaymentAuthorizedAsync(contract.Key, true, "joao");
        await Service().SetEarlyPaymentAuthorizedAsync(contract.Key, true, "joao"); // no-op

        Assert.True((await ReloadAsync(contract.Key)).EarlyPaymentAuthorized);
        var log = Assert.Single(await LogsAsync(contract.Key));
        Assert.Equal(ContractChangeLogFields.EarlyPaymentAuthorized, log.Field);
        Assert.Equal("Não", log.OldValue);
        Assert.Equal("Sim", log.NewValue);

        await Service().SetEarlyPaymentAuthorizedAsync(contract.Key, false, "joao");
        Assert.False((await ReloadAsync(contract.Key)).EarlyPaymentAuthorized);
        Assert.Equal(2, (await LogsAsync(contract.Key)).Count);
    }
}
