using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities.Market;

/// <summary>Tabela de preços de um produto/safra: cada linha é um vencimento/entrega com seus parâmetros de fórmula.</summary>
[Table("MARKET_PRICE_TABLES")]
public class MarketPriceTable
{
    [Key]
    public int Id { get; set; }

    [Column(TypeName = "VARCHAR(100) NOT NULL")]
    public required string Description { get; set; }

    /// <summary>Item do SiagroB1 (código + nome copiado, sem FK: vale em STANDALONE e SAPB1).</summary>
    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    public required string ItemCode { get; set; }

    [Column(TypeName = "VARCHAR(200) NOT NULL")]
    public required string ItemName { get; set; }

    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    public required string HarvestSeasonCode { get; set; }

    [ForeignKey(nameof(HarvestSeasonCode))]
    public HarvestSeason? HarvestSeason { get; set; }

    public bool Active { get; set; } = true;

    [Column(TypeName = "VARCHAR(100)")]
    public string? UpdatedBy { get; set; }

    public DateTime UpdatedAt { get; set; }

    public List<MarketPriceTableLine> Lines { get; set; } = [];
}
