using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SiagroB1.Domain.Entities.Market;

/// <summary>Mapa de vencimentos: mês de entrega (1–12) → mês de bolsa (1–12) com deslocamento de ano (0 ou 1).</summary>
[Table("MARKET_COMMODITY_MONTHS")]
[Index(nameof(CommodityCode), nameof(DeliveryMonth), IsUnique = true)]
public class MarketCommodityMonth
{
    [Key]
    public int Id { get; set; }

    [Column(TypeName = "VARCHAR(20) NOT NULL")]
    public required string CommodityCode { get; set; }

    [ForeignKey(nameof(CommodityCode))]
    public MarketCommodity? Commodity { get; set; }

    public int DeliveryMonth { get; set; }

    public int FuturesMonth { get; set; }

    /// <summary>0 = mesmo ano da entrega; 1 = ano seguinte.</summary>
    public int YearOffset { get; set; }
}
