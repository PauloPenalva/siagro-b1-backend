using System.Text.Json.Serialization;

namespace SiagroB1.Domain.Dtos;

/// <summary>
/// Linha do diálogo "Devolver": um item da venda e quanto dele ainda pode voltar. Quantidades em
/// <c>double</c> pelo mesmo motivo das actions: o UI5 lê <c>Edm.Decimal</c> como string. Todas as
/// propriedades carregam <c>[JsonPropertyName]</c> em PascalCase — sem isso a resposta sai em
/// camelCase e a tela encontra tudo <c>undefined</c>.
/// </summary>
public class SalesInvoiceNfeReturnableItemDto
{
    [JsonPropertyName("OriginItemKey")]
    public required string OriginItemKey { get; set; }

    [JsonPropertyName("ItemCode")]
    public required string ItemCode { get; set; }

    [JsonPropertyName("ItemName")]
    public string? ItemName { get; set; }

    [JsonPropertyName("UnitOfMeasureCode")]
    public string? UnitOfMeasureCode { get; set; }

    [JsonPropertyName("SoldQuantity")]
    public double SoldQuantity { get; set; }

    /// <summary>Em devoluções não canceladas (Pendentes inclusive).</summary>
    [JsonPropertyName("ReturnedQuantity")]
    public double ReturnedQuantity { get; set; }

    [JsonPropertyName("Returnable")]
    public double Returnable { get; set; }
}
