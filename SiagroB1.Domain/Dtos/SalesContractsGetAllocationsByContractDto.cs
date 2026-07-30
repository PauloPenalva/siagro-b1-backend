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

    [JsonPropertyName("AllocationKey")]
    public Guid? AllocationKey { get; set; }

    [JsonPropertyName("OriginLabel")]
    public string? OriginLabel { get; set; }

    [JsonPropertyName("InvoiceUnitPrice")]
    public decimal? InvoiceUnitPrice { get; set; }

    [JsonPropertyName("ContractPrice")]
    public decimal? ContractPrice { get; set; }

    [JsonPropertyName("PriceDifference")]
    public decimal? PriceDifference { get; set; }

    [JsonPropertyName("SalesShipmentReleaseRowId")]
    public int? SalesShipmentReleaseRowId { get; set; }

    [JsonPropertyName("SalesShipmentReleaseDate")]
    public DateTime? SalesShipmentReleaseDate { get; set; }

    /// <summary>Código do contrato do outro lado da realocação (origem/destino), para rastreio.</summary>
    [JsonPropertyName("CounterpartyContractCode")]
    public string? CounterpartyContractCode { get; set; }

    /// <summary>Justificativa do ajuste, preenchida nas linhas de origem "Conciliação".</summary>
    [JsonPropertyName("ReconciliationReason")]
    public string? ReconciliationReason { get; set; }
}