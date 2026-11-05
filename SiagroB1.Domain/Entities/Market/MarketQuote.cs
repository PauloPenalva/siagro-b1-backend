using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities.Market;

/// <summary>Última cotação de um instrumento (1:1), vinda do feed da CMA ou digitada manualmente.</summary>
[Table("MARKET_QUOTES")]
public class MarketQuote
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    [Column(TypeName = "VARCHAR(30) NOT NULL")]
    public required string InstrumentCode { get; set; }

    [ForeignKey(nameof(InstrumentCode))]
    public MarketInstrument? Instrument { get; set; }

    [Column(TypeName = "DECIMAL(18,6)")] public decimal? Last { get; set; }
    [Column(TypeName = "DECIMAL(18,6)")] public decimal? Bid { get; set; }
    [Column(TypeName = "DECIMAL(18,6)")] public decimal? Ask { get; set; }
    [Column(TypeName = "DECIMAL(18,6)")] public decimal? ChangePercent { get; set; }
    [Column(TypeName = "DECIMAL(18,6)")] public decimal? High { get; set; }
    [Column(TypeName = "DECIMAL(18,6)")] public decimal? Low { get; set; }
    [Column(TypeName = "DECIMAL(18,6)")] public decimal? Open { get; set; }
    [Column(TypeName = "DECIMAL(18,6)")] public decimal? PreviousClose { get; set; }

    public bool IsSettlement { get; set; }

    public DateTime QuotedAt { get; set; }

    public MarketQuoteSource Source { get; set; }

    [Column(TypeName = "VARCHAR(100)")]
    public string? UserName { get; set; }

    // Token de concorrência: o flush da CMA não pode gravar por cima de um lançamento manual feito em paralelo.
    [ConcurrencyCheck]
    public bool ManualOverride { get; set; }

    public DateTime UpdatedAt { get; set; }
}
