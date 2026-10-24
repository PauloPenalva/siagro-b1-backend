using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

/// <summary>Competência da EFD-Reinf (fechamento R-2099). Única por contribuinte e período.</summary>
[Table("REINF_PERIODS")]
public class ReinfPeriod
{
    [Key]
    public Guid Key { get; set; } = Guid.NewGuid();

    [Column(TypeName = "VARCHAR(8)")] public string TaxIdRoot { get; set; } = string.Empty;
    /// <summary>AAAA-MM.</summary>
    [Column(TypeName = "VARCHAR(7)")] public string Period { get; set; } = string.Empty;
    public ReinfPeriodStatus Status { get; set; } = ReinfPeriodStatus.Open;
    [Column(TypeName = "VARCHAR(60)")] public string? ClosingReceipt { get; set; }
    public DateTime? ClosedAt { get; set; }
}
