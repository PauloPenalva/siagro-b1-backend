using System.Text.Json.Serialization;

namespace SiagroB1.Domain.Dtos;

/// <summary>
/// Item de entrada própria autorizada que o complemento de valor próprio pode apontar (spec 2026-10-14) — a lista do
/// "Incluir item". Quantidades e valores em <c>double</c>: o UI5 lê <c>Edm.Decimal</c> como string. Todas as
/// propriedades com <c>[JsonPropertyName]</c> PascalCase — sem isso a resposta sai em camelCase e a tela lê undefined.
/// </summary>
public class PurchaseInvoiceComplementCandidateDto
{
    [JsonPropertyName("ItemKey")]
    public Guid ItemKey { get; set; }

    [JsonPropertyName("InvoiceKey")]
    public Guid InvoiceKey { get; set; }

    /// <summary>Número do documento (DE000001), o mesmo das mensagens.</summary>
    [JsonPropertyName("InvoiceNumber")]
    public string? InvoiceNumber { get; set; }

    [JsonPropertyName("InvoiceDate")]
    public DateTime? InvoiceDate { get; set; }

    [JsonPropertyName("AccessKey")]
    public string? AccessKey { get; set; }

    /// <summary>nItem do item na NF-e original.</summary>
    [JsonPropertyName("LineNumber")]
    public int? LineNumber { get; set; }

    [JsonPropertyName("ItemCode")]
    public string? ItemCode { get; set; }

    [JsonPropertyName("ItemName")]
    public string? ItemName { get; set; }

    [JsonPropertyName("UnitOfMeasure")]
    public string? UnitOfMeasure { get; set; }

    [JsonPropertyName("Quantity")]
    public double Quantity { get; set; }

    [JsonPropertyName("UnitPrice")]
    public double UnitPrice { get; set; }

    [JsonPropertyName("Total")]
    public double Total { get; set; }

    [JsonPropertyName("UsageCode")]
    public int? UsageCode { get; set; }

    /// <summary>Nome da natureza: a coluna Natureza da grade mostra o nome (fora do spec, decidido no plano).</summary>
    [JsonPropertyName("UsageName")]
    public string? UsageName { get; set; }

    /// <summary>Nulo no item sem contrato (D5): o operador escolhe o contrato na linha do complemento.</summary>
    [JsonPropertyName("PurchaseContractKey")]
    public Guid? PurchaseContractKey { get; set; }

    [JsonPropertyName("PurchaseContractCode")]
    public string? PurchaseContractCode { get; set; }
}
