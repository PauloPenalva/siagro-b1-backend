using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Um comentário do contrato de COMPRA: anotação livre com data, hora e autor, mantida na tela
/// Detail e editável a qualquer tempo — inclusive em contrato encerrado ou cancelado, porque
/// comentário não altera valor, volume nem saldo.
///
/// Não confundir com <see cref="PurchaseContract.Comments"/>, o campo escalar de "Observações"
/// do cabeçalho, editável apenas em rascunho. É por causa desse escalar que a coleção se chama
/// <see cref="PurchaseContract.CommentEntries"/>, e não <c>Comments</c>.
///
/// Toda inclusão, edição e exclusão gera linha em <see cref="PurchaseContractChangeLog"/> com o
/// código <see cref="ContractChangeLogFields.Comment"/>.
/// </summary>
[Table("PURCHASE_CONTRACTS_COMMENTS")]
public class PurchaseContractComment
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid? Key { get; set; }

    public Guid? PurchaseContractKey { get; set; }
    public virtual PurchaseContract? PurchaseContract { get; set; }

    /// <summary>
    /// Data e hora da última escrita: nasce na inclusão e é SOBRESCRITA a cada edição. O texto
    /// anterior e o momento da versão anterior ficam no log de alterações.
    /// </summary>
    public DateTime CommentedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// Autor da última escrita. Sobrescrito junto com <see cref="CommentedAt"/> quando um
    /// administrador edita o comentário de outra pessoa.
    /// </summary>
    [Column(TypeName = "VARCHAR(100)")]
    public string? CommentedBy { get; set; }

    /// <summary>
    /// 500 caracteres para casar com <see cref="PurchaseContractChangeLog.NewValue"/>: assim
    /// nenhuma linha do log sai truncada.
    /// </summary>
    [Column(TypeName = "VARCHAR(500) NOT NULL")]
    public required string CommentText { get; set; }
}
