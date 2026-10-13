using System.Text.Json.Serialization;

namespace SiagroB1.Domain.Dtos;

public class PurchaseContractTotalsResponseDto
{
    [JsonPropertyName("TotalVolume")]
    public decimal TotalVolume { get; set; }
    
    [JsonPropertyName("TotalStandard")]
    public decimal TotalStandard { get; set; }
    
    [JsonPropertyName("FixedVolume")] 
    public decimal FixedVolume { get; set; }
    
    [JsonPropertyName("AvailableVolumeToPricing")] 
    public decimal AvailableVolumeToPricing { get; set; }
    
    [JsonPropertyName("TotalPrice")]
    public decimal TotalPrice { get; set; }
    
    [JsonPropertyName("TotalTax")]
    public decimal TotalTax { get; set; }
    
    [JsonPropertyName("TotalShipmentReleases")]
    public decimal TotalShipmentReleases { get; set; }
    
    [JsonPropertyName("TotalAvailableToRelease")]
    public decimal TotalAvailableToRelease { get; set; }
    
    [JsonPropertyName("AvaiableVolume")]
    public decimal AvaiableVolume { get; set; }

    [JsonPropertyName("WashedOutVolume")]
    public decimal WashedOutVolume { get; set; }

    // Totalizador de Funrural (spec 2026-10-13, só STANDALONE; zerado em SAPB1). Base = TotalPrice.
    [JsonPropertyName("FunruralBase")]
    public decimal FunruralBase { get; set; }

    [JsonPropertyName("FunruralInssRate")]
    public decimal FunruralInssRate { get; set; }

    [JsonPropertyName("FunruralRatRate")]
    public decimal FunruralRatRate { get; set; }

    [JsonPropertyName("FunruralSenarRate")]
    public decimal FunruralSenarRate { get; set; }

    [JsonPropertyName("FunruralInssValue")]
    public decimal FunruralInssValue { get; set; }

    [JsonPropertyName("FunruralRatValue")]
    public decimal FunruralRatValue { get; set; }

    [JsonPropertyName("FunruralSenarValue")]
    public decimal FunruralSenarValue { get; set; }

    [JsonPropertyName("TotalFunrural")]
    public decimal TotalFunrural { get; set; }

    /// <summary>Base − Funrural: o que o produtor recebe (Funrural sempre retido sobre o preço cheio).</summary>
    [JsonPropertyName("NetToProducer")]
    public decimal NetToProducer { get; set; }
}