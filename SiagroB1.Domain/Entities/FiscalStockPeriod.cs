using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Período (mês) do estoque fiscal por filial. Fechado, congela as camadas PEPS (<see cref="Layers"/>)
/// e bloqueia lançamentos retroativos. Único por filial + ano + mês.
/// </summary>
[Table("FISCAL_STOCK_PERIODS")]
public class FiscalStockPeriod : BaseEntity
{
    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    public string BranchCode { get; set; } = string.Empty;

    public int Year { get; set; }

    public int Month { get; set; }

    public FiscalStockPeriodStatus Status { get; set; } = FiscalStockPeriodStatus.Open;

    public DateTime? ClosedAt { get; set; }

    [Column(TypeName = "VARCHAR(100)")]
    public string? ClosedBy { get; set; }

    public DateTime? ReopenedAt { get; set; }

    [Column(TypeName = "VARCHAR(100)")]
    public string? ReopenedBy { get; set; }

    [Column(TypeName = "VARCHAR(500)")]
    public string? ReopenReason { get; set; }

    public List<FiscalStockPeriodEvent> Events { get; set; } = new();

    public List<FiscalStockPeriodLayer> Layers { get; set; } = new();
}
