using System.Text.Json.Serialization;

namespace SiagroB1.Domain.Dtos;

/// <summary>
/// Uma devolução da recusa aguardando NF-e da carga (spec 2026-10-09 §7). O cabeçalho da recusa se repete em cada
/// linha: a tela lê o da primeira. Enums como string, como o resto das telas lê.
/// </summary>
/// <remarks>
/// Propriedades em PascalCase via <c>[JsonPropertyName]</c>, como em <see cref="ShipmentLoadRefusableDocumentDto"/>:
/// sem isso a resposta sai em camelCase e o binding do UI5 lê tudo <c>undefined</c>.
/// </remarks>
public class ShipmentLoadPendingRefusalReturnDto
{
    [JsonPropertyName("RefusalKey")]
    public string RefusalKey { get; set; } = "";

    [JsonPropertyName("Destination")]
    public string Destination { get; set; } = "";

    [JsonPropertyName("DestinationWarehouseCode")]
    public string? DestinationWarehouseCode { get; set; }

    [JsonPropertyName("DestinationWarehouseName")]
    public string? DestinationWarehouseName { get; set; }

    [JsonPropertyName("Reason")]
    public string Reason { get; set; } = "";

    [JsonPropertyName("SalesInvoiceKey")]
    public string SalesInvoiceKey { get; set; } = "";

    [JsonPropertyName("InvoiceNumber")]
    public string? InvoiceNumber { get; set; }

    [JsonPropertyName("CardName")]
    public string? CardName { get; set; }

    [JsonPropertyName("Quantity")]
    public decimal Quantity { get; set; }

    [JsonPropertyName("InvoiceStatus")]
    public string InvoiceStatus { get; set; } = "";

    [JsonPropertyName("NfeStatus")]
    public string NfeStatus { get; set; } = "";

    [JsonPropertyName("TaxDocumentNumber")]
    public string? TaxDocumentNumber { get; set; }

    [JsonPropertyName("TaxDocumentSeries")]
    public string? TaxDocumentSeries { get; set; }

    [JsonPropertyName("NfeConfirmationError")]
    public string? NfeConfirmationError { get; set; }
}
