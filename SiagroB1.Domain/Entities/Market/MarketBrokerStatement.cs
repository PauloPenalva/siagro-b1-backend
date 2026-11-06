using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities.Market;

/// <summary>Extrato diário da corretora: ajuste diário e margem (informativos).</summary>
[Table("MARKET_BROKER_STATEMENTS")]
[Index(nameof(AccountId), nameof(Date), IsUnique = true)]
public class MarketBrokerStatement
{
    [Key]
    public int Id { get; set; }

    public int AccountId { get; set; }

    [ForeignKey(nameof(AccountId))]
    public MarketCounterpartyAccount? Account { get; set; }

    [Column(TypeName = "DATE")]
    public DateTime Date { get; set; }

    [Column(TypeName = "DECIMAL(18,2)")]
    public decimal DailyAdjustment { get; set; }

    [Column(TypeName = "DECIMAL(18,2)")]
    public decimal MarginRequired { get; set; }

    [Column(TypeName = "DECIMAL(18,2)")]
    public decimal MarginDeposited { get; set; }

    public CurrencyType Currency { get; set; }

    [Column(TypeName = "VARCHAR(500)")]
    public string? Notes { get; set; }

    [Column(TypeName = "VARCHAR(100)")]
    public string? CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; }
}
