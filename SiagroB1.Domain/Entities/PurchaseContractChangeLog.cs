using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Uma alteração registrada no contrato de COMPRA. Espelho de
/// <see cref="SalesContractChangeLog"/>.
///
/// Hoje cobre apenas o ciclo de vida da fixação de preço (criação, aprovação, rejeição,
/// estorno, exclusão) — a edição pós-aprovação de observação, local de entrega e anexos não
/// existe no contrato de compra.
/// </summary>
[Table("PURCHASE_CONTRACTS_CHANGE_LOGS")]
public class PurchaseContractChangeLog
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid? Key { get; set; }

    public Guid? PurchaseContractKey { get; set; }
    public virtual PurchaseContract? PurchaseContract { get; set; }

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
    /// Valor anterior. Nulo quando a linha registra uma INCLUSÃO.
    /// </summary>
    [Column(TypeName = "VARCHAR(500)")]
    public string? OldValue { get; set; }

    /// <summary>
    /// Valor novo. Nulo quando a linha registra uma REMOÇÃO.
    /// </summary>
    [Column(TypeName = "VARCHAR(500)")]
    public string? NewValue { get; set; }
}
