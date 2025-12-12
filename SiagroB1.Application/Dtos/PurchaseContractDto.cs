using System.Text.Json.Serialization;

namespace SiagroB1.Application.Dtos;

public class PurchaseContractDto
{
    [JsonPropertyName("Key")]
    public Guid? Key { get; set; }
    
    [JsonPropertyName("Code")]
    public string? Code { get; set; }
    
    [JsonPropertyName("AvaiableVolume")]
    public decimal AvaiableVolume { get; set; }
    
    [JsonPropertyName("UnitOfMeasureCode")]
    public string UnitOfMeasureCode { get; set; }
}