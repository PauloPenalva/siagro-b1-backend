using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Uma alteração pontual feita no contrato de VENDA depois de aprovado: observação, local de
/// entrega ou anexo. Registro campo a campo — responde "quem mudou o quê, quando, e o que
/// estava lá antes".
///
/// Não cobre o ciclo de vida do contrato (rascunho, aprovação, cancelamento): para isso já
/// existem os carimbos <c>CreatedBy/At</c>, <c>UpdatedBy/At</c>, <c>ApprovedBy/At</c> e
/// <c>CanceledBy/At</c> em <see cref="Shared.Base.BaseEntity"/>.
/// </summary>
[Table("SALES_CONTRACTS_CHANGE_LOGS")]
public class SalesContractChangeLog
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid? Key { get; set; }

    public Guid? SalesContractKey { get; set; }
    public virtual SalesContract? SalesContract { get; set; }

    public DateTime ChangedAt { get; set; } = DateTime.Now;

    [Column(TypeName = "VARCHAR(100)")]
    public string? ChangedBy { get; set; }

    /// <summary>
    /// Código do campo alterado (ver <see cref="ContractChangeLogFields"/>), não o rótulo
    /// traduzido: a tela resolve o rótulo por formatter, para não travar o i18n.
    /// </summary>
    [Column(TypeName = "VARCHAR(50) NOT NULL")]
    public required string Field { get; set; }

    /// <summary>
    /// Valor anterior. Nulo quando a linha registra uma INCLUSÃO (local de entrega, anexo).
    /// </summary>
    [Column(TypeName = "VARCHAR(500)")]
    public string? OldValue { get; set; }

    /// <summary>
    /// Valor novo. Nulo quando a linha registra uma REMOÇÃO (local de entrega, anexo).
    /// </summary>
    [Column(TypeName = "VARCHAR(500)")]
    public string? NewValue { get; set; }
}
