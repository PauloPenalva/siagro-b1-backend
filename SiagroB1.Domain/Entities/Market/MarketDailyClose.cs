using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities.Market;

/// <summary>Fechamento diário de um instrumento. Único por (instrumento, data).</summary>
[Table("MARKET_DAILY_CLOSES")]
[Index(nameof(InstrumentCode), nameof(Date), IsUnique = true)]
public class MarketDailyClose
{
    [Key]
    public int Id { get; set; }

    [Column(TypeName = "VARCHAR(30) NOT NULL")]
    public required string InstrumentCode { get; set; }

    [ForeignKey(nameof(InstrumentCode))]
    public MarketInstrument? Instrument { get; set; }

    [Column(TypeName = "DATE")]
    public DateTime Date { get; set; }

    [Column(TypeName = "DECIMAL(18,6)")]
    public decimal Close { get; set; }

    public MarketQuoteSource Source { get; set; }

    public bool IsSettlement { get; set; }

    [Column(TypeName = "VARCHAR(100)")]
    public string? UserName { get; set; }

    /// <summary>Horário (UTC) da cotação que originou o fechamento; nulo no manual. Distingue fechamento velho.</summary>
    public DateTime? QuotedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
