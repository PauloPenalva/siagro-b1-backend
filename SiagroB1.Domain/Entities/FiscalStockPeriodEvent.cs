using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

/// <summary>Histórico de fechamentos e reaberturas de um período do estoque fiscal.</summary>
[Table("FISCAL_STOCK_PERIOD_EVENTS")]
public class FiscalStockPeriodEvent : BaseEntity
{
    public Guid PeriodKey { get; set; }

    public FiscalStockPeriod? Period { get; set; }

    public FiscalStockPeriodEventType EventType { get; set; }

    public DateTime At { get; set; }

    [Column(TypeName = "VARCHAR(100) NOT NULL")]
    public string By { get; set; } = string.Empty;

    [Column(TypeName = "VARCHAR(500)")]
    public string? Reason { get; set; }
}
