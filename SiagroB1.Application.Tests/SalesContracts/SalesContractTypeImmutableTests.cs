using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesContracts;

/// <summary>
/// Espelho do contrato de compra: o tipo do contrato de venda (FIX/PAF) é definido na
/// criação e o update rejeita a troca.
/// </summary>
public class SalesContractTypeImmutableTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private SalesContractsUpdateService Service() => new(
        _db.Context,
        new FakeBusinessPartnerService(new Dictionary<string, string> { ["C0001"] = "CLIENTE" }),
        new FakeItemService(new Dictionary<string, string> { ["SOJA"] = "SOJA EM GRAOS" }),
        new FakeAgentService(new Dictionary<int, string> { [1] = "AGENTE" }),
        TestNotificationOutbox.For(_db.Context),
        NullLogger<SalesContractsUpdateService>.Instance);

    private async Task<SalesContract> SeedAsync(ContractType type)
    {
        var contract = new SalesContract
        {
            Key = Guid.NewGuid(),
            Code = "SC-001",
            CardCode = "C0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "24/25",
            AgentCode = 1,
            TotalVolume = 100_000m,
            Type = type,
            Status = ContractStatus.Draft,
        };

        _db.Context.SalesContracts.Add(contract);
        await _db.Context.SaveChangesAsync();

        return contract;
    }

    /// <summary>
    /// Forma do PATCH: `entity` e `existingEntity` são a mesma instância rastreada, então só
    /// o OriginalValues do EF ainda guarda o tipo gravado no banco.
    /// </summary>
    [Fact]
    public async Task Update_ChangingTypeOnTrackedEntity_Throws()
    {
        var contract = await SeedAsync(ContractType.Fixed);

        var tracked = await _db.Context.SalesContracts.FirstAsync(c => c.Key == contract.Key);
        tracked.Type = ContractType.ToBeDetermined;

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(contract.Key, tracked, "maria"));

        Assert.Contains("tipo do contrato", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Update_ChangingTypeFromPafToFixed_Throws()
    {
        var contract = await SeedAsync(ContractType.ToBeDetermined);

        var tracked = await _db.Context.SalesContracts.FirstAsync(c => c.Key == contract.Key);
        tracked.Type = ContractType.Fixed;

        await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(contract.Key, tracked, "maria"));
    }

    [Fact]
    public async Task Update_WithoutChangingType_Succeeds()
    {
        var contract = await SeedAsync(ContractType.Fixed);

        var tracked = await _db.Context.SalesContracts.FirstAsync(c => c.Key == contract.Key);
        tracked.TotalVolume = 250_000m;

        await Service().ExecuteAsync(contract.Key, tracked, "maria");

        var reloaded = await _db.Context.SalesContracts.FirstAsync(c => c.Key == contract.Key);
        Assert.Equal(250_000m, reloaded.TotalVolume);
        Assert.Equal(ContractType.Fixed, reloaded.Type);
    }
}
