using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Uma alteração pontual feita no cadastro da carga. Registro campo a campo — responde "quem
/// mudou o quê, quando, e o que estava lá antes".
/// </summary>
/// <remarks>
/// <b>Log e Movimentação não são a mesma coisa.</b> Este log registra o que o USUÁRIO DIGITOU:
/// os campos do formulário, o comentário e o cancelamento. <c>SHIPMENT_LOAD_MOVEMENTS</c>
/// registra o que aconteceu com o SALDO: vinculação de romaneio, faturamento, recusa, devolução.
/// Manter a fronteira é o que impede as duas grades de contarem a mesma história e divergirem.
/// </remarks>
[Table("SHIPMENT_LOADS_CHANGE_LOGS")]
[Index(nameof(ShipmentLoadKey))]
public class ShipmentLoadChangeLog
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid? Key { get; set; }

    public Guid? ShipmentLoadKey { get; set; }
    public virtual ShipmentLoad? ShipmentLoad { get; set; }

    public DateTime ChangedAt { get; set; } = DateTime.Now;

    [Column(TypeName = "VARCHAR(100)")]
    public string? ChangedBy { get; set; }

    /// <summary>
    /// Código do campo alterado (ver <see cref="ShipmentLoadChangeLogFields"/>), não o rótulo
    /// traduzido: a tela resolve o rótulo por formatter, para não travar o i18n.
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
