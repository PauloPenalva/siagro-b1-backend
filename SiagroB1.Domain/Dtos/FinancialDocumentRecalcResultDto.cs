using System.Text.Json.Serialization;

namespace SiagroB1.Domain.Dtos;

public class FinancialDocumentRecalcResultDto
{
    [JsonPropertyName("Key")] public Guid Key { get; set; }
    [JsonPropertyName("Code")] public string? Code { get; set; }
    [JsonPropertyName("NetAmount")] public decimal NetAmount { get; set; }
    [JsonPropertyName("SettledAmount")] public decimal SettledAmount { get; set; }
    [JsonPropertyName("OpenAmount")] public decimal OpenAmount { get; set; }
    [JsonPropertyName("Status")] public string? Status { get; set; }
}
