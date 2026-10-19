using System.Text.Json.Serialization;

namespace SiagroB1.Domain.Dtos;

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

    [JsonPropertyName("AgentCode")]
    public int? AgentCode { get; set; }

    [JsonPropertyName("AgentName")]
    public string? AgentName { get; set; }

    [JsonPropertyName("CardCode")]
    public string? CardCode { get; set; }

    [JsonPropertyName("CardName")]
    public string? CardName { get; set; }

    [JsonPropertyName("DeliveryLocationCode")]
    public string? DeliveryLocationCode { get; set; }

    [JsonPropertyName("DeliveryLocationName")]
    public string? DeliveryLocationName { get; set; }
}