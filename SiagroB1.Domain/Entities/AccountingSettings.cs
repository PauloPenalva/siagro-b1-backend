using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Configurações contábeis da empresa (só STANDALONE). Linha única (<see cref="Key"/> = 1) por banco.
/// Sem data de corte, a contabilização automática fica desligada.
/// </summary>
[Table("ACCOUNTING_SETTINGS")]
public class AccountingSettings
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int Key { get; set; } = 1;

    /// <summary>Data de corte: nada anterior a ela é contabilizado automaticamente (já está no saldo inicial).</summary>
    [Column(TypeName = "DATE")]
    public DateTime? CutoverDate { get; set; }

    /// <summary>Conta de apuração usada quando a conta de resultado não informa a sua.</summary>
    [Column(TypeName = "VARCHAR(20)")]
    public string? DefaultResultClosingAccountCode { get; set; }

    public AccountingClosingFrequency ClosingFrequency { get; set; } = AccountingClosingFrequency.Monthly;

    public DateTime UpdatedAt { get; set; }

    [Column(TypeName = "VARCHAR(100)")]
    public string UpdatedBy { get; set; } = string.Empty;
}
