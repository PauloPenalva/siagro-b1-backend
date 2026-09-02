using System.Text.Json.Serialization;

namespace SiagroB1.Domain.Dtos;

/// <summary>
/// Linha do diálogo de recusa de carga: um documento de saída vivo da carga, com o quanto dele
/// ainda pode ser devolvido.
/// </summary>
/// <remarks>
/// <see cref="RefusableQuantity"/> é o que o diálogo pré-preenche na coluna editável — o total
/// do documento MENOS o que já foi devolvido antes, para uma segunda recusa parcial não oferecer
/// um volume que já voltou.
/// <para>
/// Todas as propriedades carregam <c>[JsonPropertyName]</c> em PascalCase: sem isso a resposta
/// sai em camelCase e o binding do UI5 encontra tudo <c>undefined</c>, sem erro nenhum.
/// </para>
/// </remarks>
public class ShipmentLoadRefusableDocumentDto
{
    [JsonPropertyName("SalesInvoiceKey")]
    public required string SalesInvoiceKey { get; set; }

    [JsonPropertyName("InvoiceNumber")]
    public string? InvoiceNumber { get; set; }

    [JsonPropertyName("InvoiceDate")]
    public DateTime? InvoiceDate { get; set; }

    [JsonPropertyName("CardCode")]
    public string? CardCode { get; set; }

    [JsonPropertyName("CardName")]
    public string? CardName { get; set; }

    [JsonPropertyName("DeliveryCardCode")]
    public string? DeliveryCardCode { get; set; }

    [JsonPropertyName("DeliveryCardName")]
    public string? DeliveryCardName { get; set; }

    [JsonPropertyName("ItemCode")]
    public string? ItemCode { get; set; }

    [JsonPropertyName("ItemName")]
    public string? ItemName { get; set; }

    [JsonPropertyName("UnitOfMeasureCode")]
    public string? UnitOfMeasureCode { get; set; }

    /// <summary>Quantidade faturada no documento.</summary>
    [JsonPropertyName("Quantity")]
    public decimal Quantity { get; set; }

    /// <summary>Quantidade já devolvida por recusas anteriores deste mesmo documento.</summary>
    [JsonPropertyName("AlreadyReturnedQuantity")]
    public decimal AlreadyReturnedQuantity { get; set; }

    /// <summary>Quanto ainda pode ser devolvido: <see cref="Quantity"/> − já devolvido.</summary>
    [JsonPropertyName("RefusableQuantity")]
    public decimal RefusableQuantity { get; set; }
}
