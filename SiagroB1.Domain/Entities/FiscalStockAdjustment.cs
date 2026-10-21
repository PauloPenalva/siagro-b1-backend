using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Ajuste manual do estoque fiscal (entrada ou saída avulsa) por filial + produto.
/// Numeração AJ000001 em sequência por filial, gerada na criação.
/// </summary>
[Table("FISCAL_STOCK_ADJUSTMENTS")]
public class FiscalStockAdjustment : BaseEntity
{
    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    public string BranchCode { get; set; } = string.Empty;

    [Column(TypeName = "VARCHAR(20) NOT NULL")]
    public string DocNumber { get; set; } = string.Empty;

    public DateTime AdjustmentDate { get; set; }

    [Column(TypeName = "VARCHAR(50) NOT NULL")]
    public string ItemCode { get; set; } = string.Empty;

    [Column(TypeName = "VARCHAR(200)")]
    public string? ItemName { get; set; }

    [Column(TypeName = "VARCHAR(4)")]
    public string? UnitOfMeasureCode { get; set; }

    public FiscalStockAdjustmentDirection Direction { get; set; }

    [Column(TypeName = "DECIMAL(19,6)")]
    public decimal Quantity { get; set; }

    [Column(TypeName = "DECIMAL(19,2)")]
    public decimal TotalValue { get; set; }

    [Column(TypeName = "VARCHAR(500) NOT NULL")]
    public string Reason { get; set; } = string.Empty;
}
