using System.Text.Json.Serialization;

namespace SiagroB1.Domain.Dtos;

public class StorageAddressBalanceDto
{
    [JsonPropertyName("Code")]
    public string?  Code { get; set; }
    
    [JsonPropertyName("CreationDate")]
    public DateTime?  CreationDate { get; set; }
    
    [JsonPropertyName("Description")]
    public string?  Description { get; set; }
    
    [JsonPropertyName("CardCode")]
    public string?  CardCode { get; set; }
    
    [JsonPropertyName("CardName")]
    public string?  CardName { get; set; }
    
    [JsonPropertyName("ItemCode")]
    public string?  ItemCode { get; set; }
    
    [JsonPropertyName("ItemName")]
    public string?  ItemName { get; set; }
    
    [JsonPropertyName("WarehouseCode")]
    public string?  WarehouseCode { get; set; }
    
    [JsonPropertyName("WarehouseName")]
    public string?  WarehouseName { get; set; }
    
    [JsonPropertyName("Balance")]
    public decimal?  Balance { get; set; }
}