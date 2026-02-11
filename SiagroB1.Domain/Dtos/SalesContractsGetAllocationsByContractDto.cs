using System.Text.Json.Serialization;

namespace SiagroB1.Domain.Dtos;

public class SalesContractsGetAllocationsByContractDto
{
    [JsonPropertyName("BranchCode")]
    public string? BranchCode { get; set; }

    [JsonPropertyName("BranchName")]
    public string? BranchName { get; set; }
    
    [JsonPropertyName("InvoiceNumber")]
    public string? InvoiceNumber { get; set; }
    
    [JsonPropertyName("InvoiceDate")]
    public DateTime? InvoiceDate { get; set; }
    
    [JsonPropertyName("NotaFiscal")]
    public string? NotaFiscal { get; set; }
    
    [JsonPropertyName("Serie")]
    public string? Serie { get; set; }
    
    [JsonPropertyName("Type")]
    public string? Type { get; set; }
    
    [JsonPropertyName("Quantity")]
    public decimal? Quantity { get; set; }
    
    [JsonPropertyName("UnitOfMeasureCode")]
    public string? UnitOfMeasureCode { get; set; }
    
    [JsonPropertyName("TruckCode")]
    public string? TruckCode { get; set; }
    
    [JsonPropertyName("DeliveredQuantity")]
    public decimal? DeliveredQuantity { get; set; }
    
    [JsonPropertyName("QuantityLoss")]
    public decimal? QuantityLoss { get; set; }
    
    [JsonPropertyName("DeliveryStatus")]
    public string? DeliveryStatus { get; set; }
}