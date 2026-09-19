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

    /// <summary>
    /// De onde veio a entrega: <c>Standard</c> (romaneio normal), <c>WarehouseLoss</c> (Compra gerada
    /// pela aprovação da Conferência de Saldo de Armazém, GAC-1164), <c>DischargeLoss</c>/
    /// <c>DischargeGain</c> (quebra/sobra de descarga gerada pela Conferência de Entregas, troca
    /// nota). O tipo continua Compra/Dev.Compra/Quebra Descarga/Sobra Descarga conforme o caso.
    /// </summary>
    [JsonPropertyName("Origin")]
    public string? Origin { get; set; }

    [JsonPropertyName("WarehouseReconciliationCode")]
    public string? WarehouseReconciliationCode { get; set; }

    [JsonPropertyName("ReasonDescription")]
    public string? ReasonDescription { get; set; }

    /// <summary>Troca nota: número fiscal da nota de venda cuja conferência gerou o romaneio 16/17.</summary>
    [JsonPropertyName("DischargeInvoiceNumber")]
    public string? DischargeInvoiceNumber { get; set; }

    /// <summary>Troca nota: código da carga faturada por essa nota.</summary>
    [JsonPropertyName("DischargeLoadCode")]
    public string? DischargeLoadCode { get; set; }
}