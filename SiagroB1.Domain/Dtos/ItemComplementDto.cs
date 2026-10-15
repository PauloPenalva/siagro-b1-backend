using System.Text.Json.Serialization;

namespace SiagroB1.Domain.Dtos;

/// <summary>
/// Complemento cadastral de um item (<see cref="Entities.ItemComplement"/>), usado pela tela de
/// Complemento do produto e pelo diálogo de faturamento (<c>/shipment-billing</c>).
/// </summary>
public class ItemComplementDto
{
    [JsonPropertyName("ItemCode")]
    public required string ItemCode { get; set; }

    [JsonPropertyName("CommercialUnitOfMeasureCode")]
    public string? CommercialUnitOfMeasureCode { get; set; }

    [JsonPropertyName("CommercialFactor")]
    public decimal? CommercialFactor { get; set; }

    [JsonPropertyName("TaxableUnitOfMeasureCode")]
    public string? TaxableUnitOfMeasureCode { get; set; }

    [JsonPropertyName("TaxableFactor")]
    public decimal? TaxableFactor { get; set; }

    [JsonPropertyName("ControlsRoyalties")]
    public bool ControlsRoyalties { get; set; }
}

/// <summary>Unidade tributável de exportação a gravar; os dois vazios limpam a configuração.</summary>
public sealed record ItemTaxableUnitInput(string? UnitOfMeasureCode, decimal? Factor);
