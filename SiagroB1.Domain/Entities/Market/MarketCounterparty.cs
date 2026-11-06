using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities.Market;

/// <summary>Corretora ou banco com quem o Mercado opera. As contas ficam em MarketCounterpartyAccount.</summary>
[Table("MARKET_COUNTERPARTIES")]
[Index(nameof(Name), IsUnique = true)]
public class MarketCounterparty
{
    [Key]
    public int Id { get; set; }

    [Column(TypeName = "VARCHAR(100) NOT NULL")]
    public required string Name { get; set; }

    public MarketCounterpartyType Type { get; set; }

    [Column(TypeName = "VARCHAR(20)")]
    public string? TaxId { get; set; }

    public bool Active { get; set; } = true;

    public List<MarketCounterpartyAccount> Accounts { get; set; } = [];
}
