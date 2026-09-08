using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Uma alteração pontual num documento financeiro — responde "quem mudou o quê, quando, e o
/// que estava lá antes". Mesma estrutura do log do documento de saída.
///
/// NÃO cobre o ciclo de vida (criado, cancelado): para isso já existem os carimbos
/// CreatedBy/At, UpdatedBy/At e CanceledBy/At da entidade base, mais CancellationReason. E não
/// cobre dinheiro: o ledger de baixas é insert-only e já é a trilha. Cobre alteração DE CAMPO —
/// na Fase 1, o vencimento e as observações.
/// </summary>
[Table("FINANCIAL_DOCUMENT_CHANGE_LOGS")]
public class FinancialDocumentChangeLog
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid? Key { get; set; }

    public Guid? FinancialDocumentKey { get; set; }

    public virtual FinancialDocument? FinancialDocument { get; set; }

    public DateTime ChangedAt { get; set; } = DateTime.Now;

    [Column(TypeName = "VARCHAR(100)")]
    public string? ChangedBy { get; set; }

    /// <summary>
    /// CÓDIGO do campo alterado, não o rótulo traduzido: a tela resolve o rótulo por formatter,
    /// para não travar o i18n.
    /// </summary>
    [Column(TypeName = "VARCHAR(50) NOT NULL")]
    public required string Field { get; set; }

    /// <summary>Valor anterior. Nulo quando a linha registra uma INCLUSÃO.</summary>
    [Column(TypeName = "VARCHAR(500)")]
    public string? OldValue { get; set; }

    /// <summary>Valor novo. Nulo quando a linha registra uma REMOÇÃO.</summary>
    [Column(TypeName = "VARCHAR(500)")]
    public string? NewValue { get; set; }
}
