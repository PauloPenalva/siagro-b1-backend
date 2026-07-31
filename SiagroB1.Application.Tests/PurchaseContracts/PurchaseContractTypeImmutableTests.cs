using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseContracts;

/// <summary>
/// O tipo do contrato de compra (FIX/PAF) é definido na criação e não muda mais: a UI só
/// habilita o campo na tela de inclusão e o update rejeita a troca vinda por PATCH/PUT.
/// </summary>
public class PurchaseContractTypeImmutableTests
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

    private async Task<PurchaseContract> SeedAsync(ContractType type)
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
            Type = type,
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

        return contract;
    }

    /// <summary>
    /// Reproduz a forma do PATCH: o controller aplica o delta sobre a entidade rastreada e
    /// passa essa mesma instância para o service, que a recarrega pelo Key. Comparar
    /// `entity` com `existingEntity` seria comparar o objeto com ele mesmo — só o
    /// OriginalValues do EF ainda guarda o tipo que está gravado.
    /// </summary>
    [Fact]
    public async Task Update_ChangingTypeOnTrackedEntity_Throws()
    {
        var contract = await SeedAsync(ContractType.Fixed);

        var tracked = await _db.Context.PurchaseContracts.FirstAsync(c => c.Key == contract.Key);
        tracked.Type = ContractType.ToBeDetermined;

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(contract.Key, tracked, "joao"));

        Assert.Contains("tipo do contrato", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Update_ChangingTypeFromPafToFixed_Throws()
    {
        var contract = await SeedAsync(ContractType.ToBeDetermined);

        var tracked = await _db.Context.PurchaseContracts.FirstAsync(c => c.Key == contract.Key);
        tracked.Type = ContractType.Fixed;

        await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(contract.Key, tracked, "joao"));
    }

    /// <summary>
    /// Edição normal: o PATCH que não mexe no tipo tem de passar. Sem isso a guarda
    /// travaria toda edição de contrato em rascunho.
    /// </summary>
    [Fact]
    public async Task Update_WithoutChangingType_Succeeds()
    {
        var contract = await SeedAsync(ContractType.Fixed);

        var tracked = await _db.Context.PurchaseContracts.FirstAsync(c => c.Key == contract.Key);
        tracked.TotalVolume = 250_000m;

        await Service().ExecuteAsync(contract.Key, tracked, "joao");

        var reloaded = await _db.Context.PurchaseContracts.FirstAsync(c => c.Key == contract.Key);
        Assert.Equal(250_000m, reloaded.TotalVolume);
        Assert.Equal(ContractType.Fixed, reloaded.Type);
    }
}
