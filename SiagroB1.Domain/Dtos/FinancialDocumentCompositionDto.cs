using System.Text.Json.Serialization;

namespace SiagroB1.Domain.Dtos;

/// <summary>
/// Function FinancialDocumentsGetComposition (spec 2026-10-13, títulos firmes). PascalCase explícito: sem os
/// atributos o ASP.NET serializa camelCase e a tela lê undefined.
/// </summary>
public class FinancialDocumentCompositionDto
{
    [JsonPropertyName("Lines")]
    public List<FinancialDocumentCompositionLineDto> Lines { get; set; } = [];

    [JsonPropertyName("Candidates")]
    public List<FinancialDocumentCompositionCandidateDto> Candidates { get; set; } = [];
}

/// <summary>Uma linha de composição: o título composto (firme ou devolução, valor assinado) num provisório.</summary>
public class FinancialDocumentCompositionLineDto
{
    [JsonPropertyName("CompositionKey")] public Guid CompositionKey { get; set; }
    [JsonPropertyName("ProvisionalDocumentKey")] public Guid ProvisionalDocumentKey { get; set; }
    [JsonPropertyName("ProvisionalCode")] public string? ProvisionalCode { get; set; }
    [JsonPropertyName("DocumentKey")] public Guid DocumentKey { get; set; }
    [JsonPropertyName("DocumentCode")] public string? DocumentCode { get; set; }
    [JsonPropertyName("Nature")] public string? Nature { get; set; }
    [JsonPropertyName("StorageTransactionCode")] public string? StorageTransactionCode { get; set; }
    [JsonPropertyName("TaxDocumentNumber")] public string? TaxDocumentNumber { get; set; }
    [JsonPropertyName("CardCode")] public string? CardCode { get; set; }
    [JsonPropertyName("CardName")] public string? CardName { get; set; }
    [JsonPropertyName("Amount")] public decimal Amount { get; set; }
    [JsonPropertyName("CreatedAt")] public DateTime? CreatedAt { get; set; }
    [JsonPropertyName("CreatedBy")] public string? CreatedBy { get; set; }
}

/// <summary>Título que pode entrar no provisório (composição) ou compensar a devolução (encontro de contas).</summary>
public class FinancialDocumentCompositionCandidateDto
{
    [JsonPropertyName("DocumentKey")] public Guid DocumentKey { get; set; }
    [JsonPropertyName("Code")] public string? Code { get; set; }
    [JsonPropertyName("Nature")] public string? Nature { get; set; }
    [JsonPropertyName("StorageTransactionCode")] public string? StorageTransactionCode { get; set; }
    [JsonPropertyName("TaxDocumentNumber")] public string? TaxDocumentNumber { get; set; }
    [JsonPropertyName("CardCode")] public string? CardCode { get; set; }
    [JsonPropertyName("CardName")] public string? CardName { get; set; }
    [JsonPropertyName("DocumentDate")] public DateTime DocumentDate { get; set; }
    [JsonPropertyName("OpenAmount")] public decimal OpenAmount { get; set; }

    /// <summary>Firme: min(saldo, espaço no provisório). Devolução: −saldo. Netting: min dos dois saldos.</summary>
    [JsonPropertyName("SuggestedAmount")] public decimal SuggestedAmount { get; set; }
}
