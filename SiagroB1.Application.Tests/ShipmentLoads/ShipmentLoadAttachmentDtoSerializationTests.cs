using System.Text.Json;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// O DTO do grid de anexos trafega em PascalCase, forçado por <c>[JsonPropertyName]</c> em TODA
/// propriedade, e <see cref="ShipmentLoadAttachmentDto.AttachmentType"/> trafega pelo NOME do
/// enum, não pelo inteiro — mesmo precedente de
/// <see cref="ShipmentLoadRefusableDocumentDtoSerializationTests"/> e
/// <see cref="SalesShipmentReleaseAvailableDtoSerializationTests"/>. Uma propriedade nova sem o
/// atributo sai em camelCase e o binding do UI5 recebe <c>undefined</c> — sem erro, sem log, só a
/// coluna em branco na tela.
/// </summary>
public class ShipmentLoadAttachmentDtoSerializationTests
{
    private static ShipmentLoadAttachmentDto Sample() => new()
    {
        Key = Guid.NewGuid(),
        AttachmentType = ShipmentLoadAttachmentType.DischargeTicket,
        Description = "Ticket de descarga 1234",
        FileName = "ticket.pdf",
        CreatedBy = "paulo",
        CreatedAt = new DateTime(2026, 9, 18),
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
    /// Sem <c>JsonStringEnumConverter</c> este valor sairia como <c>1</c> (o inteiro do enum), e a
    /// coluna "Tipo" mostraria o número em vez de "Ticket de Descarga".
    /// </summary>
    [Fact]
    public void AttachmentType_serializes_by_name_not_by_number()
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(Sample()));

        Assert.Equal("DischargeTicket", document.RootElement.GetProperty("AttachmentType").GetString());
    }
}
