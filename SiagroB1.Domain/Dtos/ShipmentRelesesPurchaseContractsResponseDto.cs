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
    
    [JsonPropertyName("FCode")]
    public string? FCode { get; set; }

    /// <summary>
    /// Origem da liberação. A tela usa este flag para esconder as colunas de contrato:
    /// numa liberação emitida por transferência de titularidade a compra já foi
    /// registrada, e o embarque não pede contrato. Ver <c>ShippingTransactionsCreateService</c>.
    /// </summary>
    [JsonPropertyName("IsOwnershipTransfer")]
    public bool IsOwnershipTransfer { get; set; }

    /// <summary>
    /// Origem da liberação como valor, e não como flag: <c>0</c> compra, <c>1</c> transferência
    /// de titularidade, <c>2</c> devolução ao armazém. A tela mostra isso numa coluna própria
    /// para que o operador saiba que aquele saldo é mercadoria que VOLTOU — reembarcá-la é uma
    /// decisão diferente de embarcar uma compra nova.
    /// </summary>
    /// <remarks>
    /// <c>IsOwnershipTransfer</c> continua exposto por compatibilidade, mas não escala: com três
    /// origens, um booleano não distingue as duas que não são compra.
    /// </remarks>
    [JsonPropertyName("Origin")]
    public int Origin { get; set; }

    /// <summary>
    /// Previsão de pagamento do contrato (<c>PURCHASE_CONTRACTS.StandardCashFlowDate</c>).
    /// É por ela que a lista é ordenada: o mais próximo de vencer embarca primeiro.
    /// </summary>
    [JsonPropertyName("StandardCashFlowDate")]
    public DateTime? StandardCashFlowDate { get; set; }
}