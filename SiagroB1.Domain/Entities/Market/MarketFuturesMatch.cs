using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities.Market;

/// <summary>Casamento PEPS entre uma operação de abertura e uma de fechamento, com o resultado realizado.</summary>
[Table("MARKET_FUTURES_MATCHES")]
public class MarketFuturesMatch
{
    [Key]
    public int Id { get; set; }

    public int AccountId { get; set; }

    [Column(TypeName = "VARCHAR(30) NOT NULL")]
    public required string InstrumentCode { get; set; }

    public int OpenTradeId { get; set; }

    [ForeignKey(nameof(OpenTradeId))]
    public MarketFuturesTrade? OpenTrade { get; set; }

    public int CloseTradeId { get; set; }

    [ForeignKey(nameof(CloseTradeId))]
    public MarketFuturesTrade? CloseTrade { get; set; }

    public int Lots { get; set; }

    [Column(TypeName = "DECIMAL(18,6)")]
    public decimal OpenPrice { get; set; }

    [Column(TypeName = "DECIMAL(18,6)")]
    public decimal ClosePrice { get; set; }

    [Column(TypeName = "DECIMAL(18,2)")]
    public decimal RealizedPnl { get; set; }

    public CurrencyType Currency { get; set; }

    [Column(TypeName = "DATE")]
    public DateTime CloseDate { get; set; }
}
