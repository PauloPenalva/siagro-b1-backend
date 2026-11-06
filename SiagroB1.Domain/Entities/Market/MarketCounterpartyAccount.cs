using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SiagroB1.Domain.Entities.Market;

/// <summary>Conta do Mercado numa corretora ou banco.</summary>
[Table("MARKET_COUNTERPARTY_ACCOUNTS")]
[Index(nameof(CounterpartyId), nameof(Code), IsUnique = true)]
public class MarketCounterpartyAccount
{
    [Key]
    public int Id { get; set; }

    public int CounterpartyId { get; set; }

    [ForeignKey(nameof(CounterpartyId))]
    public MarketCounterparty? Counterparty { get; set; }

    [Column(TypeName = "VARCHAR(30) NOT NULL")]
    public required string Code { get; set; }

    [Column(TypeName = "VARCHAR(100)")]
    public string? Description { get; set; }

    public bool Active { get; set; } = true;
}
