using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Uma alteração pontual feita no documento de saída. Registro campo a campo — responde "quem mudou
/// o quê, quando, e o que estava lá antes".
///
/// Hoje só recebe linhas de comentário (<see cref="ContractChangeLogFields.Comment"/>), a única
/// coisa que se altera no documento depois de ele existir. A estrutura é a mesma do log do contrato
/// justamente para poder receber outros campos depois sem migração de dados.
///
/// Não cobre o ciclo de vida do documento (pendente, confirmado, cancelado): para isso já existem
/// os carimbos <c>CreatedBy/At</c>, <c>UpdatedBy/At</c> e <c>CanceledBy/At</c> da entidade base.
/// </summary>
[Table("SALES_INVOICES_CHANGE_LOGS")]
public class SalesInvoiceChangeLog
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid? Key { get; set; }

    public Guid? SalesInvoiceKey { get; set; }
    public virtual SalesInvoice? SalesInvoice { get; set; }

    /// <summary>
    /// Item do documento a que a alteração se refere, quando ela é de LINHA (conferência de
    /// entrega). Nulo nas alterações de cabeçalho — as linhas de <see cref="ContractChangeLogFields.Comment"/>
    /// já gravadas continuam só com <see cref="SalesInvoiceKey"/>, por isso a coluna é nulável.
    ///
    /// Sem propriedade de navegação de volta: o diálogo da conferência consulta o entity set
    /// <c>SalesInvoicesChangeLogs</c> filtrando por esta chave, e não a rota aninhada
    /// <c>SalesInvoicesItems(key)/ChangeLogs</c>, que precisaria ser declarada à mão no controller.
    /// </summary>
    public Guid? SalesInvoiceItemKey { get; set; }

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
