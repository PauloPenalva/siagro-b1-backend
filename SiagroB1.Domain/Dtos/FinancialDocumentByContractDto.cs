using System.Text.Json.Serialization;

namespace SiagroB1.Domain.Dtos;

public class FinancialDocumentByContractDto
{
    [JsonPropertyName("Key")] public Guid Key { get; set; }
    [JsonPropertyName("Code")] public string? Code { get; set; }
    [JsonPropertyName("Nature")] public string? Nature { get; set; }
    [JsonPropertyName("Status")] public string? Status { get; set; }
    [JsonPropertyName("DueDate")] public DateTime DueDate { get; set; }
    [JsonPropertyName("NetAmount")] public decimal NetAmount { get; set; }
    [JsonPropertyName("SettledAmount")] public decimal SettledAmount { get; set; }
    [JsonPropertyName("OpenAmount")] public decimal OpenAmount { get; set; }
    [JsonPropertyName("ComposedAmount")] public decimal ComposedAmount { get; set; }
    [JsonPropertyName("AppliedAmount")] public decimal AppliedAmount { get; set; }
    [JsonPropertyName("AvailableAdvanceAmount")] public decimal AvailableAdvanceAmount { get; set; }
    [JsonPropertyName("StorageTransactionCode")] public string? StorageTransactionCode { get; set; }
    [JsonPropertyName("TaxDocumentNumber")] public string? TaxDocumentNumber { get; set; }
}
