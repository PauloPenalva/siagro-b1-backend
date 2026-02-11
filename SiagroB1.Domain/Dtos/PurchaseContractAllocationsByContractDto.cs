using System.Text.Json.Serialization;

namespace SiagroB1.Domain.Dtos;

public class PurchaseContractAllocationsByContractDto
{
    [JsonPropertyName("BranchCode")]
    public string? BranchCode { get; set; }
    
    [JsonPropertyName("BranchName")]
    public string? BranchName { get; set; }
    
    [JsonPropertyName("StorageTransactionCode")]
    public string? StorageTransactionCode { get; set; }
    
    [JsonPropertyName("StorageTransactionDate")]
    public DateTime? StorageTransactionDate { get; set; }
    
    [JsonPropertyName("StorageTransactionTypeName")]
    public string? StorageTransactionTypeName { get; set; }
    
    [JsonPropertyName("ContractNumber")]
    public string? ContractNumber { get; set; }
    
    [JsonPropertyName("Volume")]
    public decimal? Volume { get; set; }
    
    [JsonPropertyName("UnitOfMeasureCode")]
    public string? UnitOfMeasureCode { get; set; }
    
    [JsonPropertyName("WarehouseCode")]
    public string? WarehouseCode { get; set; }
    
    [JsonPropertyName("WarehouseName")]
    public string? WarehouseName { get; set; }
    
    [JsonPropertyName("TruckCode")]
    public string? TruckCode { get; set; }
    
    [JsonPropertyName("SupplierCode")]
    public string? SupplierCode { get; set; }
    
    [JsonPropertyName("SupplierName")]
    public string? SupplierName { get; set; }
    
    [JsonPropertyName("NotaFiscalVenda")]
    public string? NotaFiscalVenda { get; set; }
    
    
}