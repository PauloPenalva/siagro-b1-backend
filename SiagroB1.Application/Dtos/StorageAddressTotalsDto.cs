using System.Text.Json.Serialization;

namespace SiagroB1.Application.Dtos;

public class StorageAddressTotalsDto
{
    [JsonPropertyName("TotalReceipt")]
    public decimal TotalReceipt { get; set; }
    
    [JsonPropertyName("TotalShipment")]
    public decimal TotalShipment { get; set; }
    
    [JsonPropertyName("TotalQualityLoss")]
    public decimal TotalQualityLoss { get; set; }
    
    [JsonPropertyName("Balance")]
    public decimal Balance { get; set;  }
}