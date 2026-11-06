using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities.Market;

/// <summary>Commodity da posição (SOY, CORN...). O código é o mesmo de MarketInstrument.Commodity.</summary>
[Table("MARKET_COMMODITIES")]
public class MarketCommodity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    [Column(TypeName = "VARCHAR(20) NOT NULL")]
    public required string Code { get; set; }

    [Column(TypeName = "VARCHAR(60) NOT NULL")]
    public required string Name { get; set; }

    /// <summary>Bolsa em que a commodity é protegida.</summary>
    public MarketExchange HedgeExchange { get; set; }

    public bool Active { get; set; } = true;

    public List<MarketCommodityItem> Items { get; set; } = [];

    public List<MarketCommodityMonth> Months { get; set; } = [];
}
