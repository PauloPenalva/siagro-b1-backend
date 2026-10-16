using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesContracts;

/// <summary>
/// Contrato de preço fixo editado depois de retirar a aprovação: a fixação automática é o
/// espelho do contrato e tem de acompanhar volume, preço e frete. Sem isso a reaprovação gera
/// o título provisório com o volume antigo (caso CV000002 da CEAGUI: 3.000.000 → 2.359.780).
/// </summary>
public class SalesContractUpdateSyncsAutoFixationTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private SalesContractsUpdateService Service() => new(
        _db.Context,
        new FakeBusinessPartnerService(new Dictionary<string, string> { ["C0001"] = "CLIENTE" }),
        new FakeItemService(new Dictionary<string, string> { ["SOJA"] = "SOJA EM GRAOS" }),
        new FakeAgentService(new Dictionary<int, string> { [1] = "AGENTE" }),
        TestNotificationOutbox.For(_db.Context),
        NullLogger<SalesContractsUpdateService>.Instance);

    private async Task<SalesContract> SeedAsync(ContractType type, bool withFixation = true)
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
            TotalVolume = 3_000_000m,
            Price = 1.65m,
            FreightCostStandard = 90m,
            FixedVolume = 3_000_000m,
            Type = type,
            Status = ContractStatus.Draft,
        };

        _db.Context.SalesContracts.Add(contract);
        if (withFixation)
        {
            _db.Context.SalesContractsPriceFixations.Add(new SalesContractPriceFixation
            {
                Key = Guid.NewGuid(),
                SalesContractKey = contract.Key,
                FixationVolume = 3_000_000m,
                FixationPrice = 1.65m,
                FreightCost = 90m,
                Status = PriceFixationStatus.Confirmed,
            });
        }
        await _db.Context.SaveChangesAsync();

        return contract;
    }

    [Fact]
    public async Task Update_FixedContract_SyncsAutoFixationAndFixedVolume()
    {
        var contract = await SeedAsync(ContractType.Fixed);

        var tracked = await _db.Context.SalesContracts.FirstAsync(c => c.Key == contract.Key);
        tracked.TotalVolume = 2_359_780m;
        tracked.Price = 1.70m;
        tracked.FreightCostStandard = 95m;

        await Service().ExecuteAsync(contract.Key, tracked, "maria");

        var fixation = await _db.Context.SalesContractsPriceFixations
            .SingleAsync(f => f.SalesContractKey == contract.Key);
        Assert.Equal(2_359_780m, fixation.FixationVolume);
        Assert.Equal(1.70m, fixation.FixationPrice);
        Assert.Equal(95m, fixation.FreightCost);
        Assert.Equal(PriceFixationStatus.Confirmed, fixation.Status);

        var reloaded = await _db.Context.SalesContracts.FirstAsync(c => c.Key == contract.Key);
        Assert.Equal(2_359_780m, reloaded.FixedVolume);
    }

    /// <summary>PAF não tem fixação automática: as fixações dele são negociadas uma a uma.</summary>
    [Fact]
    public async Task Update_PafContract_DoesNotTouchFixations()
    {
        var contract = await SeedAsync(ContractType.ToBeDetermined);

        var tracked = await _db.Context.SalesContracts.FirstAsync(c => c.Key == contract.Key);
        tracked.TotalVolume = 4_000_000m;

        await Service().ExecuteAsync(contract.Key, tracked, "maria");

        var fixation = await _db.Context.SalesContractsPriceFixations
            .SingleAsync(f => f.SalesContractKey == contract.Key);
        Assert.Equal(3_000_000m, fixation.FixationVolume);
    }
}
