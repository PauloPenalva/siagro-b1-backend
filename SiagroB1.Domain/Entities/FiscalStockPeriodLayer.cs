using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Camada PEPS remanescente congelada no fechamento do período (um registro por camada com saldo).
/// </summary>
[Table("FISCAL_STOCK_PERIOD_LAYERS")]
public class FiscalStockPeriodLayer : BaseEntity
{
    public Guid PeriodKey { get; set; }

    public FiscalStockPeriod? Period { get; set; }

    [Column(TypeName = "VARCHAR(50) NOT NULL")]
    public string ItemCode { get; set; } = string.Empty;

    [Column(TypeName = "VARCHAR(200)")]
    public string? ItemName { get; set; }

    [Column(TypeName = "VARCHAR(4)")]
    public string? UnitOfMeasureCode { get; set; }

    public DateTime LayerDate { get; set; }

    /// <summary>Linha de origem (documento fiscal ou ajuste) que gerou a camada.</summary>
    public Guid SourceLineKey { get; set; }

    [Column(TypeName = "DECIMAL(19,6)")]
    public decimal Quantity { get; set; }

    [Column(TypeName = "DECIMAL(19,2)")]
    public decimal Value { get; set; }

    /// <summary>Ordem da camada dentro do produto (PEPS).</summary>
    public int Sequence { get; set; }
}
