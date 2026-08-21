using System.Text.Json.Serialization;

using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Dtos;

/// <summary>
/// Linha do dialog de faturamento (<c>/shipment-billing</c>): uma liberação de venda
/// disponível para o produto embarcado, já enriquecida com dados do contrato de origem
/// (cliente, preço, UoM) necessários para montar a <c>SalesInvoice</c>.
/// </summary>
public class SalesShipmentReleaseAvailableDto
{
    [JsonPropertyName("SalesShipmentReleaseKey")]
    public required string SalesShipmentReleaseKey { get; set; }

    [JsonPropertyName("RowId")]
    public int RowId { get; set; }

    [JsonPropertyName("BranchShortName")]
    public string? BranchShortName { get; set; }

    [JsonPropertyName("SalesContractKey")]
    public required string SalesContractKey { get; set; }

    [JsonPropertyName("SalesContractCode")]
    public string? SalesContractCode { get; set; }

    [JsonPropertyName("Complement")]
    public string? Complement { get; set; }

    [JsonPropertyName("CardCode")]
    public string? CardCode { get; set; }

    [JsonPropertyName("CardName")]
    public string? CardName { get; set; }

    [JsonPropertyName("CardFName")]
    public string? CardFName { get; set; }

    [JsonPropertyName("ItemCode")]
    public required string ItemCode { get; set; }

    [JsonPropertyName("ItemName")]
    public string? ItemName { get; set; }

    [JsonPropertyName("UnitOfMeasureCode")]
    public string? UnitOfMeasureCode { get; set; }

    [JsonPropertyName("Price")]
    public decimal Price { get; set; }

    [JsonPropertyName("DeliveryLocationCode")]
    public string? DeliveryLocationCode { get; set; }

    [JsonPropertyName("DeliveryLocationName")]
    public string? DeliveryLocationName { get; set; }

    [JsonPropertyName("AvailableQuantity")]
    public decimal AvailableQuantity { get; set; }

    /// <summary>
    /// Situação do contrato de venda. Sempre <c>Approved</c> na lista — vem no DTO para a tela
    /// exibir, não para filtrar de novo.
    /// </summary>
    [JsonPropertyName("SalesContractStatus")]
    public ContractStatus? SalesContractStatus { get; set; }

    /// <summary>
    /// Saldo do contrato de venda, podendo ser NEGATIVO quando a lista é pedida com
    /// <c>includeContractsWithoutBalance</c>. A tela mostra o número para o usuário decidir:
    /// o faturamento não valida saldo de contrato.
    /// </summary>
    [JsonPropertyName("SalesContractAvailableVolume")]
    public decimal SalesContractAvailableVolume { get; set; }
}
