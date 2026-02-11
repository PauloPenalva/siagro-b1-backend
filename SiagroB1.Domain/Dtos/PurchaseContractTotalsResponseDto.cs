using System.Text.Json.Serialization;

namespace SiagroB1.Domain.Dtos;

public class PurchaseContractTotalsResponseDto
{
    [JsonPropertyName("TotalVolume")]
    public decimal TotalVolume { get; set; }
    
    [JsonPropertyName("TotalStandard")]
    public decimal TotalStandard { get; set; }
    
    [JsonPropertyName("FixedVolume")] 
    public decimal FixedVolume { get; set; }
    
    [JsonPropertyName("AvailableVolumeToPricing")] 
    public decimal AvailableVolumeToPricing { get; set; }
    
    [JsonPropertyName("TotalPrice")]
    public decimal TotalPrice { get; set; }
    
    [JsonPropertyName("TotalTax")]
    public decimal TotalTax { get; set; }
    
    [JsonPropertyName("TotalShipmentReleases")]
    public decimal TotalShipmentReleases { get; set; }
    
    [JsonPropertyName("TotalAvailableToRelease")]
    public decimal TotalAvailableToRelease { get; set; }
    
    [JsonPropertyName("AvaiableVolume")]
    public decimal AvaiableVolume { get; set; }
}