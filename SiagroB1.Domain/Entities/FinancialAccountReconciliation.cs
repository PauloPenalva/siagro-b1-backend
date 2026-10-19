using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Reconciliação da conta financeira contra o extrato do banco numa data. As linhas conciliadas (baixas e lançamentos
/// manuais) apontam para cá por ReconciliationKey. Fecha só quando saldo anterior conciliado + soma marcada == saldo do
/// extrato. No máximo uma aberta por conta (índice único filtrado).
/// </summary>
[Table("FINANCIAL_ACCOUNT_RECONCILIATIONS")]
[Index(nameof(FinancialAccountCode), nameof(StatementDate))]
public class FinancialAccountReconciliation : BaseEntity
{
    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    [ForeignKey(nameof(FinancialAccount))]
    public string FinancialAccountCode { get; set; } = string.Empty;
    public virtual FinancialAccount? FinancialAccount { get; set; }

    public DateTime StatementDate { get; set; }

    /// <summary>Saldo final informado no extrato do banco.</summary>
    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")]
    public decimal StatementBalance { get; set; }

    /// <summary>Saldo conciliado das reconciliações fechadas anteriores, gravado na abertura.</summary>
    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")]
    public decimal OpeningBalance { get; set; }

    public FinancialAccountReconciliationStatus Status { get; set; }

    public DateTime? ClosedAt { get; set; }

    [Column(TypeName = "VARCHAR(500)")]
    public string? Notes { get; set; }
}
