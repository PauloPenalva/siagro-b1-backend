using System.Text.Json.Serialization;

namespace SiagroB1.Domain.Dtos;

/// <summary>
/// Complemento fiscal do contrato de compra, com nome e alíquotas da natureza só para exibição.
/// PascalCase explícito: function com <c>Returns&lt;IActionResult&gt;</c> sai camelCase sem isto.
/// </summary>
public class PurchaseContractFiscalComplementDto
{
    [JsonPropertyName("PurchaseContractKey")]
    public Guid PurchaseContractKey { get; set; }

    [JsonPropertyName("UsageCode")]
    public int? UsageCode { get; set; }

    [JsonPropertyName("UsageName")]
    public string? UsageName { get; set; }

    [JsonPropertyName("FunruralInssRate")]
    public decimal? FunruralInssRate { get; set; }

    [JsonPropertyName("FunruralRatRate")]
    public decimal? FunruralRatRate { get; set; }

    [JsonPropertyName("FunruralSenarRate")]
    public decimal? FunruralSenarRate { get; set; }

    [JsonPropertyName("UpdatedAt")]
    public DateTime? UpdatedAt { get; set; }

    [JsonPropertyName("UpdatedBy")]
    public string? UpdatedBy { get; set; }

    /// <summary>Natureza preenchida — é o que a aprovação exige.</summary>
    [JsonPropertyName("IsComplete")]
    public bool IsComplete { get; set; }
}
