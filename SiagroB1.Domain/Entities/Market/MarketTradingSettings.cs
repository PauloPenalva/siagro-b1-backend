using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities.Market;

/// <summary>Parâmetros de negociação do Mercado. Linha única (<see cref="Key"/> = 1) por banco de empresa.</summary>
[Table("MARKET_TRADING_SETTINGS")]
public class MarketTradingSettings
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int Key { get; set; } = 1;

    /// <summary>Instrumento do dólar spot (ponto de partida da curva).</summary>
    [Column(TypeName = "VARCHAR(30)")]
    public string? SpotFxInstrumentCode { get; set; }

    /// <summary>Volume (t) a partir do qual a boleta exige a diretoria. Nulo = regra desligada.</summary>
    [Column(TypeName = "DECIMAL(18,3)")]
    public decimal? BoardApprovalVolumeTons { get; set; }

    /// <summary>Desvio (%) do preço negociado a partir do qual a boleta exige a diretoria. Nulo = regra desligada.</summary>
    [Column(TypeName = "DECIMAL(18,4)")]
    public decimal? BoardApprovalDeviationPercent { get; set; }

    /// <summary>Unidade de medida do contrato gerado pela boleta.</summary>
    [Column(TypeName = "VARCHAR(4)")]
    public string? ContractUnitOfMeasureCode { get; set; }

    /// <summary>Unidade de medida do frete no contrato gerado pela boleta.</summary>
    [Column(TypeName = "VARCHAR(4)")]
    public string? ContractFreightUmCode { get; set; }
}
