using System.Text.Json.Serialization;

namespace SiagroB1.Domain.Dtos;

public class ShipmentRelesesPurchaseContractsResponseDto
{
    [JsonPropertyName("ShipmentReleaseKey")]
    public required string ShipmentReleaseKey { get; set; }
    
    [JsonPropertyName("BranchShortName")]
    public required string BranchShortName { get; set; }

    [JsonPropertyName("PurchaseContractCode")]
    public required string PurchaseContractCode { get; set; }
    
    [JsonPropertyName("RowId")]
    public int RowId { get; set; }

    [JsonPropertyName("TaxId")]
    public string? TaxId { get; set; }
    
    [JsonPropertyName("FName")]
    public string? FName { get; set; }
    
    [JsonPropertyName("Notes")]
    public string? Notes { get; set; }
    
    [JsonPropertyName("City")]
    public string? City { get; set; }
    
    [JsonPropertyName("State")]
    public string? State { get; set; }
    
    [JsonPropertyName("DeliveryLocationCode")]
    public required string DeliveryLocationCode { get; set; }
    
    [JsonPropertyName("DeliveryLocationName")]
    public required string DeliveryLocationName { get; set; }
    
    [JsonPropertyName("ItemCode")]
    public required string ItemCode { get; set; }
    
    [JsonPropertyName("ItemName")]
    public required string ItemName { get; set; }
    
    [JsonPropertyName("UnitOfMeasureCode")]
    public required string UnitOfMeasureCode { get; set; }
    
    [JsonPropertyName("AvailableQuantity")]
    public decimal AvailableQuantity { get; set; }
}