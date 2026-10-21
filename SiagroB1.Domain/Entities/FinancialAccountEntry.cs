using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Interfaces;
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
[Index(nameof(TransferKey))]
public class FinancialAccountEntry : BaseEntity, IDimensionFields
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

    /// <summary>Preenchida quando a linha nasceu de uma transferência entre contas.</summary>
    [ForeignKey(nameof(Transfer))]
    public Guid? TransferKey { get; set; }
    public virtual FinancialAccountTransfer? Transfer { get; set; }

    /// <summary>Dimensão gerencial 1 (centro de custo). Sem FK: o cadastro é dual-mode.</summary>
    [Column(TypeName = "VARCHAR(10)")]
    public string? Dim1Code { get; set; }

    [Column(TypeName = "VARCHAR(10)")]
    public string? Dim2Code { get; set; }

    [Column(TypeName = "VARCHAR(10)")]
    public string? Dim3Code { get; set; }

    [Column(TypeName = "VARCHAR(10)")]
    public string? Dim4Code { get; set; }

    [Column(TypeName = "VARCHAR(10)")]
    public string? Dim5Code { get; set; }

    /// <summary>Conta contábil. Sem FK: o cadastro é dual-mode (OACT/LEDGER_ACCOUNTS).</summary>
    [Column(TypeName = "VARCHAR(20)")]
    public string? LedgerAccountCode { get; set; }
}
