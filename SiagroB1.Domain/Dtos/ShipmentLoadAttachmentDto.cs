using System.Text.Json.Serialization;

using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Dtos;

/// <summary>Linha do grid de anexos da carga — sem o binário.</summary>
/// <remarks>
/// Todas as propriedades carregam <c>[JsonPropertyName]</c> em PascalCase: sem isso a resposta sai
/// em camelCase e o binding do UI5 encontra tudo <c>undefined</c>, sem erro nenhum. E sem
/// <see cref="JsonStringEnumConverter"/> em <see cref="AttachmentType"/>, o enum sai como INTEIRO,
/// e a coluna "Tipo" mostraria o número em vez do rótulo pt-BR que o formatter do frontend busca
/// pelo nome. Mesmo precedente de <see cref="SalesShipmentReleaseAvailableDto"/> e
/// <see cref="ShipmentLoadRefusableDocumentDto"/>.
/// </remarks>
public class ShipmentLoadAttachmentDto
{
    [JsonPropertyName("Key")]
    public Guid? Key { get; set; }

    [JsonPropertyName("AttachmentType")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ShipmentLoadAttachmentType AttachmentType { get; set; }

    [JsonPropertyName("Description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("FileName")]
    public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("CreatedBy")]
    public string? CreatedBy { get; set; }

    [JsonPropertyName("CreatedAt")]
    public DateTime? CreatedAt { get; set; }
}
