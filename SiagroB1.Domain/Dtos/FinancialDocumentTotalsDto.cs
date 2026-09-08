using System.Text.Json.Serialization;

namespace SiagroB1.Domain.Dtos;

public class FinancialDocumentTotalsDto
{
    [JsonPropertyName("OpenAmount")] public decimal OpenAmount { get; set; }
    [JsonPropertyName("OverdueAmount")] public decimal OverdueAmount { get; set; }
    [JsonPropertyName("DueAmount")] public decimal DueAmount { get; set; }
    [JsonPropertyName("DocumentCount")] public int DocumentCount { get; set; }
}
