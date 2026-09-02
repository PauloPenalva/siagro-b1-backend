using System.Text.Json;
using SiagroB1.Domain.Dtos;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// O DTO do diálogo de recusa trafega em PascalCase, forçado por <c>[JsonPropertyName]</c> em
/// TODA propriedade. Uma propriedade nova sem o atributo sai em camelCase e o binding do UI5
/// recebe <c>undefined</c> — sem erro, sem log, só a coluna em branco na tela. Mesmo precedente
/// de <see cref="SalesShipmentReleaseAvailableDtoSerializationTests"/>.
/// </summary>
public class ShipmentLoadRefusableDocumentDtoSerializationTests
{
    private static ShipmentLoadRefusableDocumentDto Sample() => new()
    {
        SalesInvoiceKey = Guid.NewGuid().ToString(),
        InvoiceNumber = "000000501",
        InvoiceDate = new DateTime(2026, 9, 1),
        CardCode = "C0001",
        CardName = "CLIENTE TESTE",
        DeliveryCardCode = "D0001",
        DeliveryCardName = "PORTO",
        ItemCode = "SOJA",
        ItemName = "SOJA EM GRAOS",
        UnitOfMeasureCode = "KG",
        Quantity = 40_000m,
        AlreadyReturnedQuantity = 15_000m,
        RefusableQuantity = 25_000m,
    };

    [Fact]
    public void Every_property_serializes_in_PascalCase()
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(Sample()));

        var camelCased = document.RootElement
            .EnumerateObject()
            .Select(p => p.Name)
            .Where(name => char.IsLower(name[0]))
            .ToArray();

        Assert.Empty(camelCased);
    }

    /// <summary>
    /// Os três números que a grade lê por nome. <c>RefusableQuantity</c> é o que o diálogo
    /// pré-preenche na coluna editável.
    /// </summary>
    [Fact]
    public void The_quantity_fields_are_present_by_their_expected_names()
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(Sample()));

        Assert.Equal(40_000m, document.RootElement.GetProperty("Quantity").GetDecimal());
        Assert.Equal(15_000m, document.RootElement.GetProperty("AlreadyReturnedQuantity").GetDecimal());
        Assert.Equal(25_000m, document.RootElement.GetProperty("RefusableQuantity").GetDecimal());
    }
}
