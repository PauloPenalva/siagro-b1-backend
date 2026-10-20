using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Transferência entre contas financeiras. Gera duas linhas em FINANCIAL_ACCOUNT_ENTRIES (saída na origem, entrada no
/// destino) ligadas por TransferKey. O valor é sempre positivo; o sinal fica nas linhas.
/// </summary>
[Table("FINANCIAL_ACCOUNT_TRANSFERS")]
[Index(nameof(TransferDate))]
[Index(nameof(FromAccountCode))]
[Index(nameof(ToAccountCode))]
public class FinancialAccountTransfer : BaseEntity
{
    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    [ForeignKey(nameof(FromAccount))]
    public string FromAccountCode { get; set; } = string.Empty;
    public virtual FinancialAccount? FromAccount { get; set; }

    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    [ForeignKey(nameof(ToAccount))]
    public string ToAccountCode { get; set; } = string.Empty;
    public virtual FinancialAccount? ToAccount { get; set; }

    public DateTime TransferDate { get; set; }

    /// <summary>Sempre positivo.</summary>
    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")]
    public decimal Amount { get; set; }

    [Column(TypeName = "VARCHAR(200) NOT NULL")]
    public string Description { get; set; } = string.Empty;

    [Column(TypeName = "VARCHAR(500)")]
    public string? Notes { get; set; }

    public virtual ICollection<FinancialAccountEntry> Entries { get; set; } = new List<FinancialAccountEntry>();
}
