using System.Text.Json.Serialization;

namespace SiagroB1.Domain.Dtos;

/// <summary>
/// Complemento cadastral de um armazém (<see cref="Entities.WarehouseComplement"/>), usado pela
/// tela de Complemento do armazém.
/// </summary>
public class WarehouseComplementDto
{
    [JsonPropertyName("WarehouseCode")]
    public required string WarehouseCode { get; set; }

    [JsonPropertyName("IsParticipant")]
    public bool IsParticipant { get; set; }

    [JsonPropertyName("IsOwn")]
    public bool IsOwn { get; set; }

    [JsonPropertyName("Notes")]
    public string? Notes { get; set; }
}
