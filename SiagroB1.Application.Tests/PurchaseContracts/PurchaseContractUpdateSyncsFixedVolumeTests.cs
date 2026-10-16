using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseContracts;

/// <summary>
/// No contrato de preço fixo o volume fixado é o volume do contrato (a fixação automática
/// cobre tudo). A edição já sincronizava a fixação, mas deixava o FixedVolume parado — na
/// CEAGUI todo contrato de compra fixo mostrava "Volume fixado" 0.
/// </summary>
public class PurchaseContractUpdateSyncsFixedVolumeTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private PurchaseContractsUpdateService Service() => new(
        _db.Context,
        new FakeBusinessPartnerService(new Dictionary<string, string> { ["F0001"] = "FORNECEDOR" }),
        new FakeItemService(new Dictionary<string, string> { ["SOJA"] = "SOJA EM GRAOS" }),
        new FakeWarehouseService(new Dictionary<string, string> { ["01"] = "ARMAZEM 01" }),
        new FakeAgentService(new Dictionary<int, string> { [1] = "AGENTE" }),
        TestNotificationOutbox.For(_db.Context),
        NullLogger<PurchaseContractsUpdateService>.Instance);

    [Fact]
    public async Task Update_FixedContract_SetsFixedVolumeToTotalVolume()
    {
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(),
            Code = "PC-001",
            CardCode = "F0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "24/25",
            DeliveryLocationCode = "01",
            AgentCode = 1,
            TotalVolume = 100_000m,
            StandardPrice = 2m,
            Type = ContractType.Fixed,
            Status = ContractStatus.Draft,
        };
        _db.Context.PurchaseContracts.Add(contract);
        _db.Context.PurchaseContractsPriceFixations.Add(new PurchaseContractPriceFixation
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = contract.Key,
            FixationVolume = 100_000m,
            FixationPrice = 2m,
            Status = PriceFixationStatus.Confirmed,
        });
        await _db.Context.SaveChangesAsync();

        var tracked = await _db.Context.PurchaseContracts.FirstAsync(c => c.Key == contract.Key);
        tracked.TotalVolume = 80_000m;

        await Service().ExecuteAsync(contract.Key, tracked, "joao");

        var reloaded = await _db.Context.PurchaseContracts.FirstAsync(c => c.Key == contract.Key);
        Assert.Equal(80_000m, reloaded.FixedVolume);
    }
}
