using System.Text.Json.Serialization;

namespace SiagroB1.Domain.Dtos;

/// <summary>Complemento fiscal do contrato de venda (<see cref="Entities.SalesContractFiscalComplement"/>), com nomes só para exibição.</summary>
public class SalesContractFiscalComplementDto
{
    [JsonPropertyName("SalesContractKey")]
    public Guid SalesContractKey { get; set; }

    [JsonPropertyName("UsageCode")]
    public int? UsageCode { get; set; }

    [JsonPropertyName("UsageName")]
    public string? UsageName { get; set; }

    [JsonPropertyName("PaymentConditionCode")]
    public int? PaymentConditionCode { get; set; }

    [JsonPropertyName("PaymentConditionName")]
    public string? PaymentConditionName { get; set; }

    [JsonPropertyName("AdditionalInfo")]
    public string? AdditionalInfo { get; set; }

    [JsonPropertyName("CustomerOrderNumber")]
    public string? CustomerOrderNumber { get; set; }

    [JsonPropertyName("CustomerOrderItem")]
    public string? CustomerOrderItem { get; set; }

    [JsonPropertyName("UpdatedAt")]
    public DateTime? UpdatedAt { get; set; }

    [JsonPropertyName("UpdatedBy")]
    public string? UpdatedBy { get; set; }

    /// <summary>Natureza e condição de pagamento preenchidas.</summary>
    [JsonPropertyName("IsComplete")]
    public bool IsComplete { get; set; }
}
