using System.Text.Json;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// O DTO da lista de liberações trafega com nomes em PascalCase, forçados por
/// <c>[JsonPropertyName]</c> em TODA propriedade. Uma propriedade nova sem o atributo sai em
/// camelCase e o binding do UI5 que a lê por nome recebe <c>undefined</c> — sem erro, sem log,
/// só a coluna em branco na tela.
/// </summary>
public class SalesShipmentReleaseAvailableDtoSerializationTests
{
    [Fact]
    public void Every_property_serializes_in_PascalCase()
    {
        var dto = new SalesShipmentReleaseAvailableDto
        {
            SalesShipmentReleaseKey = Guid.NewGuid().ToString(),
            SalesContractKey = Guid.NewGuid().ToString(),
            ItemCode = "SOJA",
            SalesContractStatus = ContractStatus.Approved,
            SalesContractAvailableVolume = -1_000m,
        };

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(dto));

        var camelCased = document.RootElement
            .EnumerateObject()
            .Select(p => p.Name)
            .Where(name => char.IsLower(name[0]))
            .ToArray();

        Assert.Empty(camelCased);
    }

    [Fact]
    public void The_contract_balance_fields_are_present_by_their_expected_names()
    {
        var dto = new SalesShipmentReleaseAvailableDto
        {
            SalesShipmentReleaseKey = Guid.NewGuid().ToString(),
            SalesContractKey = Guid.NewGuid().ToString(),
            ItemCode = "SOJA",
            SalesContractStatus = ContractStatus.Approved,
            SalesContractAvailableVolume = -1_000m,
        };

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(dto));

        Assert.True(document.RootElement.TryGetProperty("SalesContractStatus", out _));
        Assert.True(document.RootElement.TryGetProperty("SalesContractAvailableVolume", out var balance));
        Assert.Equal(-1_000m, balance.GetDecimal());
    }
}
