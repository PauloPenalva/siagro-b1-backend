using System.Text.Json.Serialization;

namespace SiagroB1.Domain.Dtos;

/// <summary>
/// Prévia do vínculo de NF do SAP com a carga (GAC-1226): tipo do vínculo, alerta e os números da comparação
/// entre o efetivo da carga e as NFs. <c>LinkType</c> é o valor inteiro de <c>SapLinkType</c>.
/// </summary>
/// <remarks>
/// <c>[JsonPropertyName]</c> em PascalCase em todas as propriedades: sem isso a resposta sai em camelCase e o
/// binding do UI5 encontra tudo <c>undefined</c>.
/// </remarks>
public class SapLinkPreviewDto
{
    [JsonPropertyName("LinkType")]
    public int LinkType { get; set; }

    [JsonPropertyName("Message")]
    public string? Message { get; set; }

    [JsonPropertyName("EffectiveQuantity")]
    public decimal EffectiveQuantity { get; set; }

    [JsonPropertyName("InvoiceQuantity")]
    public decimal InvoiceQuantity { get; set; }

    [JsonPropertyName("QuantityDifference")]
    public decimal QuantityDifference { get; set; }

    [JsonPropertyName("EffectiveValue")]
    public decimal? EffectiveValue { get; set; }

    [JsonPropertyName("InvoiceValue")]
    public decimal InvoiceValue { get; set; }

    [JsonPropertyName("ValueDifference")]
    public decimal? ValueDifference { get; set; }
}
