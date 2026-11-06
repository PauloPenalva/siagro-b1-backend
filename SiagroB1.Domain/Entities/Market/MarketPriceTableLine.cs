using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities.Market;

/// <summary>Linha da tabela de preços: instrumento, destino e parâmetros da fórmula única de preço na origem.</summary>
[Table("MARKET_PRICE_TABLE_LINES")]
public class MarketPriceTableLine
{
    [Key]
    public int Id { get; set; }

    public int PriceTableId { get; set; }

    [ForeignKey(nameof(PriceTableId))]
    public MarketPriceTable? PriceTable { get; set; }

    [Column(TypeName = "VARCHAR(100) NOT NULL")]
    public required string Description { get; set; }

    /// <summary>Instrumento da cotação (bolsa) usada no preço.</summary>
    [Column(TypeName = "VARCHAR(30) NOT NULL")]
    public required string InstrumentCode { get; set; }

    public MarketInstrument? Instrument { get; set; }

    /// <summary>Ponto da curva de dólar; nulo quando o instrumento é cotado em R$.</summary>
    [Column(TypeName = "VARCHAR(30)")]
    public string? FxCurveInstrumentCode { get; set; }

    public MarketInstrument? FxCurveInstrument { get; set; }

    public int DestinationId { get; set; }

    [ForeignKey(nameof(DestinationId))]
    public MarketDestination? Destination { get; set; }

    /// <summary>Prêmio, na unidade da cotação.</summary>
    [Column(TypeName = "DECIMAL(18,6)")]
    public decimal Premium { get; set; }

    [Column(TypeName = "DECIMAL(18,6)")]
    public decimal ElevationCost { get; set; }

    [Column(TypeName = "DECIMAL(18,6)")]
    public decimal FxDiscount { get; set; }

    [Column(TypeName = "DECIMAL(18,6)")]
    public decimal DiscountValue { get; set; }

    /// <summary>Verdadeiro: DiscountValue é percentual; falso: valor em R$/saca.</summary>
    public bool DiscountIsPercent { get; set; }

    [Column(TypeName = "DECIMAL(18,6)")]
    public decimal MarginPercent { get; set; }

    [Column(TypeName = "DECIMAL(18,6)")]
    public decimal DailyInterestPercent { get; set; }

    [Column(TypeName = "DECIMAL(18,6)")]
    public decimal TechnicalLossPercent { get; set; }

    [Column(TypeName = "DECIMAL(18,6)")]
    public decimal RuralTaxPercent { get; set; }

    [Column(TypeName = "DECIMAL(18,8)")]
    public decimal FreightCoefficientPercent { get; set; }

    [Column(TypeName = "DATE")]
    public DateTime? DeliveryStartDate { get; set; }

    [Column(TypeName = "DATE")]
    public DateTime? DeliveryEndDate { get; set; }

    [Column(TypeName = "DATE")]
    public DateTime? ReceiptDate { get; set; }

    [Column(TypeName = "DATE")]
    public DateTime? PaymentDate { get; set; }

    public int SortOrder { get; set; }

    public bool Active { get; set; } = true;
}
