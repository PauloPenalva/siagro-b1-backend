using System.Text.Json.Serialization;

using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Dtos;

/// <summary>
/// Linha do diálogo "Vincular NF do SAP": uma A/R Invoice do SAP que pode ser vinculada à carga.
/// </summary>
/// <remarks>
/// Todas as propriedades carregam <c>[JsonPropertyName]</c> em PascalCase: sem isso a resposta
/// sai em camelCase e o binding do UI5 encontra tudo <c>undefined</c>, sem erro nenhum.
/// O local de entrega NÃO está aqui: vem da liberação de venda escolhida depois.
/// </remarks>
public class SapLinkableInvoiceDto
{
    [JsonPropertyName("DocEntry")]
    public int DocEntry { get; set; }

    [JsonPropertyName("DocNum")]
    public int DocNum { get; set; }

    [JsonPropertyName("DocDate")]
    public DateTime DocDate { get; set; }

    [JsonPropertyName("CardCode")]
    public string? CardCode { get; set; }

    [JsonPropertyName("CardName")]
    public string? CardName { get; set; }

    /// <summary>Número da NF-e com 9 dígitos (mesmo formato gravado em SALES_INVOICES).</summary>
    [JsonPropertyName("TaxDocumentNumber")]
    public string? TaxDocumentNumber { get; set; }

    /// <summary>Série com 3 dígitos.</summary>
    [JsonPropertyName("TaxDocumentSeries")]
    public string? TaxDocumentSeries { get; set; }

    [JsonPropertyName("Quantity")]
    public decimal Quantity { get; set; }

    [JsonPropertyName("UnitPrice")]
    public decimal UnitPrice { get; set; }

    [JsonPropertyName("LineTotal")]
    public decimal LineTotal { get; set; }

    [JsonPropertyName("Cfop")]
    public string? Cfop { get; set; }

    /// <summary>Natureza de operação da linha da invoice (INV1.Usage); nula quando o SAP não informa.</summary>
    [JsonPropertyName("UsageCode")]
    public int? UsageCode { get; set; }

    /// <summary>Nome da natureza no OUSG; nulo quando o código não existe lá.</summary>
    [JsonPropertyName("UsageName")]
    public string? UsageName { get; set; }

    [JsonPropertyName("CarrierCode")]
    public string? CarrierCode { get; set; }

    [JsonPropertyName("FreightTerms")]
    public FreightTerms FreightTerms { get; set; }

    /// <summary>Chave de acesso da NF-e; só vem quando há add-on fiscal configurado.</summary>
    [JsonPropertyName("AccessKey")]
    public string? AccessKey { get; set; }
}
