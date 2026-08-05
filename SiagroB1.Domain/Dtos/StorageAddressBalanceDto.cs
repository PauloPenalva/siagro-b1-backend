using System.Text.Json.Serialization;

namespace SiagroB1.Domain.Dtos;

public class StorageAddressBalanceDto
{
    [JsonPropertyName("BranchCode")]
    public string? BranchCode { get; set; }
    
    [JsonPropertyName("BranchName")]
    public string? BranchName { get; set; }
        
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
    
    [JsonPropertyName("UoM")]
    public string? UoM { get; set; }

    /// <summary>
    /// Propriedade da mercadoria no lote (<see cref="Enums.StorageOwnershipType"/>).
    /// O assistente de transferência escolhe os lotes desta lista e não tem outra
    /// forma de saber se pode habilitar o vínculo de contrato de compra.
    /// </summary>
    [JsonPropertyName("OwnershipType")]
    public int OwnershipType { get; set; }
}