using System.Text.Json.Serialization;

namespace SiagroB1.Application.Dtos;

public class PurchaseContractTotalsDto
{
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
}