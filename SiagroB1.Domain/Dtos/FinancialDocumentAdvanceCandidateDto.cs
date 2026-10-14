using System.Text.Json.Serialization;

namespace SiagroB1.Domain.Dtos;

/// <summary>
/// Function FinancialDocumentsGetAdvanceCandidates (spec 2026-10-14): adiantamento que pode ser compensado no provisório.
/// PascalCase explícito: sem os atributos o ASP.NET serializa camelCase e a tela lê undefined.
/// </summary>
public class FinancialDocumentAdvanceCandidateDto
{
    [JsonPropertyName("DocumentKey")] public Guid DocumentKey { get; set; }
    [JsonPropertyName("Code")] public string? Code { get; set; }
    [JsonPropertyName("DocumentDate")] public DateTime DocumentDate { get; set; }
    [JsonPropertyName("SettledAmount")] public decimal SettledAmount { get; set; }
    [JsonPropertyName("AppliedAmount")] public decimal AppliedAmount { get; set; }
    [JsonPropertyName("AvailableAdvanceAmount")] public decimal AvailableAdvanceAmount { get; set; }

    /// <summary>min(disponível para baixa do provisório, crédito do adiantamento) — regra 4.</summary>
    [JsonPropertyName("SuggestedAmount")] public decimal SuggestedAmount { get; set; }
}
