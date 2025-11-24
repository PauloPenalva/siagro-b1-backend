using System.Text.Json.Serialization;

namespace SiagroB1.Application.Dtos;

public class WeighingTicketsQualityInspectionsDto
{
    [JsonPropertyName("Key")]
    public required Guid Key { get; set; }
    
    [JsonPropertyName("QualityAttribCode")]
    public required string QualityAttribCode { get; set; }
    
    [JsonPropertyName("Value")]
    public decimal Value { get; set; } = 0;
}