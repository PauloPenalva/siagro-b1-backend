using System.Text.Json.Serialization;

namespace SiagroB1.Application.Dtos;

public class SalesContractTotalsResponseDto
{
    [JsonPropertyName("TotalVolume")]
    public decimal TotalVolume { get; set; }
    
    [JsonPropertyName("TotalPrice")]
    public decimal TotalPrice { get; set; }
    
}