using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities.Market;

/// <summary>
/// Limite de exposição por commodity (t) ou dólar (US$), no total (Year e Month nulos) ou num mês.
/// Estourado quando o líquido passa de MaxLong ou fica abaixo de -MaxShort (ambos positivos e opcionais).
/// </summary>
[Table("MARKET_EXPOSURE_LIMITS")]
public class MarketExposureLimit
{
    [Key]
    public int Id { get; set; }

    public MarketLimitScope Scope { get; set; }

    [Column(TypeName = "VARCHAR(20)")]
    public string? CommodityCode { get; set; }

    [ForeignKey(nameof(CommodityCode))]
    public MarketCommodity? Commodity { get; set; }

    public int? Year { get; set; }

    public int? Month { get; set; }

    [Column(TypeName = "DECIMAL(18,2)")]
    public decimal? MaxLong { get; set; }

    [Column(TypeName = "DECIMAL(18,2)")]
    public decimal? MaxShort { get; set; }

    public bool Active { get; set; } = true;

    [Column(TypeName = "VARCHAR(100)")]
    public string? UpdatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }
}
