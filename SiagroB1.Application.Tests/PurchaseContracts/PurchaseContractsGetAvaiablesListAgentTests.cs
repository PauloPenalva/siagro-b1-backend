using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseContracts;

/// <summary>
/// O diálogo "Contratos disponíveis" da Alocação de Romaneios mostra a coluna
/// Comprador (GAC-1125). O romaneio não tem agente, então o dado só chega ali
/// pela projeção deste DTO.
/// </summary>
public class PurchaseContractsGetAvaiablesListAgentTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private PurchaseContractsGetService Service() =>
        new(_db, NullLogger<PurchaseContractsGetService>.Instance);

    private async Task SeedAsync(int? agentCode, string? agentName)
    {
        _db.Context.PurchaseContracts.Add(new PurchaseContract
        {
            Key = Guid.NewGuid(), Code = "PC-001", CardCode = "F0001", ItemCode = "SOJA",
            UnitOfMeasureCode = "KG", HarvestSeasonCode = "24/25", DeliveryLocationCode = "01",
            TotalVolume = 1000m, AllocatedVolume = 0m, Status = ContractStatus.Approved,
            AgentCode = agentCode, AgentName = agentName,
        });
        await _db.Context.SaveChangesAsync();
    }

    [Fact]
    public async Task ContractWithAgent_DtoCarriesCodeAndName()
    {
        await SeedAsync(5, "JOAO COMPRADOR");

        var dto = Assert.Single(Service().GetAvaiablesPurchaseContracts("F0001", "SOJA"));

        Assert.Equal(5, dto.AgentCode);
        Assert.Equal("JOAO COMPRADOR", dto.AgentName);
    }

    [Fact]
    public async Task ContractWithoutAgent_DtoCarriesNulls()
    {
        await SeedAsync(null, null);

        var dto = Assert.Single(Service().GetAvaiablesPurchaseContracts("F0001", "SOJA"));

        Assert.Null(dto.AgentCode);
        Assert.Null(dto.AgentName);
    }
}
