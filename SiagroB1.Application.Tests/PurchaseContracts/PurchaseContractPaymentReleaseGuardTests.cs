using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseContracts;

/// <summary>
/// O status de liberação de pagamento e a autorização de pagamento antecipado só mudam pelo
/// PurchaseContractsSetPaymentReleaseStatusService (permissão + log). Cópia, criação e PATCH
/// de rascunho não podem carregá-los.
/// </summary>
public class PurchaseContractPaymentReleaseGuardTests
{
    private static readonly Guid SeriesKey = Guid.NewGuid();
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();
    private readonly FakeDocNumberSequenceService _numbers = new();

    private PurchaseContractsCreateService CreateService() => new(
        _db.Context,
        new FakeBusinessPartnerService(names: new() { ["F0001"] = "PRODUTOR" }),
        new FakeItemService(new() { ["SOJA"] = "SOJA EM GRAOS" }),
        new FakeAgentService(new() { [1] = "AGENTE" }),
        new FakeWarehouseService(new() { ["01"] = "ARM 01" }),
        _numbers,
        TestNotificationOutbox.For(_db.Context),
        new FakeStringLocalizer<Resource>(),
        NullLogger<PurchaseContractsCreateService>.Instance);

    private PurchaseContractsUpdateService UpdateService() => new(
        _db.Context,
        new FakeBusinessPartnerService(new Dictionary<string, string> { ["F0001"] = "FORNECEDOR" }),
        new FakeItemService(new Dictionary<string, string> { ["SOJA"] = "SOJA EM GRAOS" }),
        new FakeWarehouseService(new Dictionary<string, string> { ["01"] = "ARMAZEM 01" }),
        new FakeAgentService(new Dictionary<int, string> { [1] = "AGENTE" }),
        TestNotificationOutbox.For(_db.Context),
        NullLogger<PurchaseContractsUpdateService>.Instance);

    private static PurchaseContract NewContract() => new()
    {
        Key = Guid.NewGuid(), DocNumberKey = SeriesKey,
        CardCode = "F0001", ItemCode = "SOJA", UnitOfMeasureCode = "KG", HarvestSeasonCode = "24/25",
        DeliveryLocationCode = "01", AgentCode = 1, TotalVolume = 1_000m, StandardPrice = 2m,
        Type = ContractType.Fixed,
    };

    [Fact]
    public async Task Copy_ResetsPaymentReleaseStatusAndEarlyPayment()
    {
        var original = NewContract();
        original.PaymentReleaseStatus = PaymentReleaseStatus.Approved;
        original.EarlyPaymentAuthorized = true;
        _db.Context.PurchaseContracts.Add(original);
        await _db.SaveChangesAsync();

        await new PurchaseContractsCopyService(_db.Context, CreateService(), _numbers)
            .ExecuteAsync(original.Key, "tester");

        var copy = await _db.Context.PurchaseContracts.AsNoTracking().SingleAsync(c => c.Key != original.Key);
        Assert.Null(copy.PaymentReleaseStatus);
        Assert.False(copy.EarlyPaymentAuthorized);
        Assert.False(await _db.Context.Set<PurchaseContractPaymentRelease>()
            .AnyAsync(r => r.PurchaseContractKey == copy.Key));
    }

    [Fact]
    public async Task Create_IgnoresPaymentReleaseFieldsFromPayload()
    {
        var contract = NewContract();
        contract.PaymentReleaseStatus = PaymentReleaseStatus.Approved;
        contract.EarlyPaymentAuthorized = true;

        await CreateService().ExecuteAsync(contract, "tester");

        var saved = await _db.Context.PurchaseContracts.AsNoTracking().SingleAsync(c => c.Key == contract.Key);
        Assert.Null(saved.PaymentReleaseStatus);
        Assert.False(saved.EarlyPaymentAuthorized);
    }

    [Fact]
    public async Task Update_KeepsStoredPaymentReleaseFieldsWhenPayloadChangesThem()
    {
        var contract = NewContract();
        contract.Code = "PC-001";
        _db.Context.PurchaseContracts.Add(contract);
        _db.Context.PurchaseContractsPriceFixations.Add(new PurchaseContractPriceFixation
        {
            Key = Guid.NewGuid(), PurchaseContractKey = contract.Key,
            FixationVolume = 1_000m, FixationPrice = 2m, Status = PriceFixationStatus.Confirmed,
        });
        await _db.Context.SaveChangesAsync();

        var tracked = await _db.Context.PurchaseContracts.FirstAsync(c => c.Key == contract.Key);
        tracked.PaymentReleaseStatus = PaymentReleaseStatus.Approved;
        tracked.EarlyPaymentAuthorized = true;
        tracked.TotalVolume = 2_000m;

        await UpdateService().ExecuteAsync(contract.Key, tracked, "joao");

        var saved = await _db.Context.PurchaseContracts.AsNoTracking().SingleAsync(c => c.Key == contract.Key);
        Assert.Null(saved.PaymentReleaseStatus);
        Assert.False(saved.EarlyPaymentAuthorized);
        Assert.Equal(2_000m, saved.TotalVolume);
    }
}
