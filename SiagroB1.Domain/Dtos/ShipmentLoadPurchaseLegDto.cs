using System.Text.Json.Serialization;

namespace SiagroB1.Domain.Dtos;

/// <summary>
/// Perna de COMPRA (8) de uma Saída (7) vinculada à carga, lida de SHIPPING_TRANSACTIONS (GAC-1175).
/// O grid da carga mostra este código porque é ele que aparece nas Entregas do contrato de compra.
/// </summary>
/// <remarks>PascalCase explícito: sem isso a resposta sai em camelCase e o binding do UI5 lê undefined.</remarks>
public class ShipmentLoadPurchaseLegDto
{
    [JsonPropertyName("SalesStorageTransactionKey")]
    public string SalesStorageTransactionKey { get; set; } = string.Empty;

    [JsonPropertyName("PurchaseStorageTransactionKey")]
    public string PurchaseStorageTransactionKey { get; set; } = string.Empty;

    [JsonPropertyName("PurchaseCode")]
    public string? PurchaseCode { get; set; }
}
