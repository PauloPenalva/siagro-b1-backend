using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Período (mês) do calendário contábil da empresa (CTG + CQD do Protheus). Fechado ou bloqueado, não
/// aceita lançamento com data dentro dele. Data sem período cadastrado conta como aberta.
/// </summary>
[Table("ACCOUNTING_PERIODS")]
public class AccountingPeriod
{
    [Key]
    public int Id { get; set; }

    public int Year { get; set; }

    public int Month { get; set; }

    [Column(TypeName = "DATE")]
    public DateTime StartDate { get; set; }

    [Column(TypeName = "DATE")]
    public DateTime EndDate { get; set; }

    public AccountingPeriodStatus Status { get; set; } = AccountingPeriodStatus.Open;

    public DateTime? ClosedAt { get; set; }

    [Column(TypeName = "VARCHAR(100)")]
    public string? ClosedBy { get; set; }

    public DateTime? ReopenedAt { get; set; }

    [Column(TypeName = "VARCHAR(100)")]
    public string? ReopenedBy { get; set; }

    [Column(TypeName = "VARCHAR(500)")]
    public string? ReopenReason { get; set; }
}
