using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities.Market;

/// <summary>Instrumento de mercado (futuro, spot ou ponto de curva). O código é o símbolo no feed da CMA.</summary>
[Table("MARKET_INSTRUMENTS")]
public class MarketInstrument
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    [Column(TypeName = "VARCHAR(30) NOT NULL")]
    public required string Code { get; set; }

    public int? CmaSourceId { get; set; }

    [Column(TypeName = "VARCHAR(100) NOT NULL")]
    public required string Description { get; set; }

    public MarketExchange Exchange { get; set; }

    public MarketInstrumentType Type { get; set; }

    /// <summary>SOY, CORN, USD...</summary>
    [Column(TypeName = "VARCHAR(20) NOT NULL")]
    public required string Commodity { get; set; }

    /// <summary>Item do SiagroB1 associado, quando houver.</summary>
    [Column(TypeName = "VARCHAR(50)")]
    public string? ItemCode { get; set; }

    [Column(TypeName = "DATE")]
    public DateTime? ExpirationDate { get; set; }

    public MarketQuoteUnit QuoteUnit { get; set; }

    /// <summary>Número de unidades por lote.</summary>
    [Column(TypeName = "DECIMAL(18,6)")]
    public decimal ContractSize { get; set; }

    /// <summary>Multiplica a cotação para obter US$/t ou R$/t (soja 0,36745; milho 0,393678).</summary>
    [Column(TypeName = "DECIMAL(18,6)")]
    public decimal TonFactor { get; set; }

    /// <summary>Dias do ponto da curva (somente CurvePoint).</summary>
    public int? CurveDays { get; set; }

    /// <summary>Aba do terminal onde o instrumento aparece.</summary>
    [Column(TypeName = "VARCHAR(40)")]
    public string? GroupName { get; set; }

    public int SortOrder { get; set; }

    public bool Active { get; set; } = true;
}
