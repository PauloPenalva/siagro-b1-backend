using System.Text.Json.Serialization;

namespace SiagroB1.Domain.Dtos;

/// <summary>
/// Linha do diálogo "Vincular NF de devolução": um A/R Credit Memo do SAP (ORIN) que pode ser vinculado a um
/// documento de devolução da carga (GAC-1226).
/// </summary>
/// <remarks>
/// Todas as propriedades carregam <c>[JsonPropertyName]</c> em PascalCase: sem isso a resposta sai em camelCase
/// e o binding do UI5 encontra tudo <c>undefined</c>, sem erro nenhum.
/// </remarks>
public class SapLinkableCreditMemoDto
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

    /// <summary>Natureza de operação da linha (RIN1.Usage); nula quando o SAP não informa.</summary>
    [JsonPropertyName("UsageCode")]
    public int? UsageCode { get; set; }

    /// <summary>Nome da natureza no OUSG; nulo quando o código não existe lá.</summary>
    [JsonPropertyName("UsageName")]
    public string? UsageName { get; set; }

    /// <summary>Chave de acesso da NF-e; só vem quando há add-on fiscal configurado.</summary>
    [JsonPropertyName("AccessKey")]
    public string? AccessKey { get; set; }

    /// <summary>DocEntry da A/R Invoice de origem (RIN1.BaseEntry com BaseType 13); nulo quando a nota não referencia.</summary>
    [JsonPropertyName("BaseInvoiceDocEntry")]
    public int? BaseInvoiceDocEntry { get; set; }

    /// <summary>A nota referencia a invoice vinculada ao documento de origem desta devolução. Só destaca; nunca é exigido.</summary>
    [JsonPropertyName("MatchesOrigin")]
    public bool MatchesOrigin { get; set; }

    /// <summary>A quantidade da nota é igual à do documento de devolução. Só as iguais podem ser vinculadas.</summary>
    [JsonPropertyName("QuantityMatches")]
    public bool QuantityMatches { get; set; }
}
