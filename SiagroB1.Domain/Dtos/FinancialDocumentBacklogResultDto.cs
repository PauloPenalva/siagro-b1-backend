using System.Text.Json.Serialization;

namespace SiagroB1.Domain.Dtos;

public class FinancialDocumentBacklogResultDto
{
    [JsonPropertyName("DryRun")] public bool DryRun { get; set; }
    [JsonPropertyName("EligibleFixations")] public int EligibleFixations { get; set; }
    [JsonPropertyName("Generated")] public int Generated { get; set; }
    [JsonPropertyName("SkippedAlreadyGenerated")] public int SkippedAlreadyGenerated { get; set; }
    [JsonPropertyName("SkippedWithoutDueDate")] public int SkippedWithoutDueDate { get; set; }
}
