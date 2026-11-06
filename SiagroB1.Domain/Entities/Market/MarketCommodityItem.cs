using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SiagroB1.Domain.Entities.Market;

/// <summary>Produto (item) do SiagroB1 que pertence a uma commodity. Um produto só pode estar em uma commodity.</summary>
[Table("MARKET_COMMODITY_ITEMS")]
[Index(nameof(ItemCode), IsUnique = true)]
public class MarketCommodityItem
{
    [Key]
    public int Id { get; set; }

    [Column(TypeName = "VARCHAR(20) NOT NULL")]
    public required string CommodityCode { get; set; }

    [ForeignKey(nameof(CommodityCode))]
    public MarketCommodity? Commodity { get; set; }

    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    public required string ItemCode { get; set; }

    [Column(TypeName = "VARCHAR(200)")]
    public string? ItemName { get; set; }
}
