using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Lançamento manual na conta financeira (tarifa, depósito, rendimento, saldo inicial...), sem título por trás.
/// Valor ASSINADO: positivo é entrada, negativo é saída. Junto com as baixas do ledger forma o extrato da conta.
/// Conciliado (<see cref="ReconciliationKey"/> preenchida) fica bloqueado: não se exclui.
/// </summary>
[Table("FINANCIAL_ACCOUNT_ENTRIES")]
[Index(nameof(FinancialAccountCode))]
[Index(nameof(EntryDate))]
[Index(nameof(ReconciliationKey))]
public class FinancialAccountEntry : BaseEntity
{
    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    [ForeignKey(nameof(FinancialAccount))]
    public string FinancialAccountCode { get; set; } = string.Empty;
    public virtual FinancialAccount? FinancialAccount { get; set; }

    public DateTime EntryDate { get; set; }

    /// <summary>ASSINADO: entrada positiva, saída negativa.</summary>
    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")]
    public decimal Amount { get; set; }

    [Column(TypeName = "VARCHAR(200) NOT NULL")]
    public string Description { get; set; } = string.Empty;

    [Column(TypeName = "VARCHAR(50)")]
    public string? DocumentReference { get; set; }

    [Column(TypeName = "VARCHAR(500)")]
    public string? Notes { get; set; }

    [ForeignKey(nameof(Reconciliation))]
    public Guid? ReconciliationKey { get; set; }
    public virtual FinancialAccountReconciliation? Reconciliation { get; set; }
}
