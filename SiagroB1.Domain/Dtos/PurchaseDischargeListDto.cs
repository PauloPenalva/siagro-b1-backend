using System.Text.Json.Serialization;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Dtos;

/// <summary>
/// Linha da tela "Ajustes de Descarga" (troca nota): um romaneio de quebra/sobra de descarga.
/// PascalCase e enum por NOME explícitos — a API serializa camelCase por padrão e o tipo TS leria
/// undefined em silêncio.
/// </summary>
public class PurchaseDischargeListDto
{
    [JsonPropertyName("Key")] public Guid Key { get; set; }
    [JsonPropertyName("Code")] public string? Code { get; set; }

    [JsonPropertyName("TransactionType")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public StorageTransactionType TransactionType { get; set; }

    [JsonPropertyName("Status")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public StorageTransactionsStatus Status { get; set; }

    [JsonPropertyName("TransactionDate")] public DateTime? TransactionDate { get; set; }
    [JsonPropertyName("SupplierCode")] public string? SupplierCode { get; set; }
    [JsonPropertyName("SupplierName")] public string? SupplierName { get; set; }
    [JsonPropertyName("PurchaseContractKey")] public Guid? PurchaseContractKey { get; set; }
    [JsonPropertyName("PurchaseContractCode")] public string? PurchaseContractCode { get; set; }
    [JsonPropertyName("ShipmentLoadCode")] public string? ShipmentLoadCode { get; set; }
    [JsonPropertyName("SalesInvoiceNumber")] public string? SalesInvoiceNumber { get; set; }
    [JsonPropertyName("Volume")] public decimal Volume { get; set; }
    [JsonPropertyName("UnitOfMeasureCode")] public string? UnitOfMeasureCode { get; set; }
    [JsonPropertyName("CreatedBy")] public string? CreatedBy { get; set; }
}
