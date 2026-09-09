using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Um comentário da carga: anotação livre com data, hora e autor, editável a qualquer tempo —
/// inclusive em carga cancelada, porque comentário não altera peso, saldo nem valor.
///
/// Não confundir com <see cref="ShipmentLoad.Comments"/>, o campo escalar de "Observações" do
/// cabeçalho. É por causa desse escalar que a coleção se chama
/// <see cref="ShipmentLoad.CommentEntries"/>, e não <c>Comments</c>.
///
/// Toda inclusão, edição e exclusão gera linha em <see cref="ShipmentLoadChangeLog"/> com o
/// código <see cref="ShipmentLoadChangeLogFields.Comment"/>.
/// </summary>
[Table("SHIPMENT_LOADS_COMMENTS")]
[Index(nameof(ShipmentLoadKey))]
public class ShipmentLoadComment
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid? Key { get; set; }

    public Guid? ShipmentLoadKey { get; set; }
    public virtual ShipmentLoad? ShipmentLoad { get; set; }

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
    /// 500 caracteres para casar com <see cref="ShipmentLoadChangeLog.NewValue"/>: assim nenhuma
    /// linha do log sai truncada.
    /// </summary>
    [Column(TypeName = "VARCHAR(500) NOT NULL")]
    public required string CommentText { get; set; }
}
