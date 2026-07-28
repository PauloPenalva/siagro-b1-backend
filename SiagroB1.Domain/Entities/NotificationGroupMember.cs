using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Uma pessoa dentro de um <see cref="NotificationGroup"/>.
/// </summary>
[Table("NOTIFICATION_GROUP_MEMBERS")]
[Index(nameof(NotificationGroupKey))]
[Index(nameof(NotificationGroupKey), nameof(PhoneE164), IsUnique = true)]
public class NotificationGroupMember : BaseEntity
{
    public Guid NotificationGroupKey { get; set; }
    public virtual NotificationGroup? NotificationGroup { get; set; }

    [Column(TypeName = "VARCHAR(100) NOT NULL")]
    public required string Name { get; set; }

    /// <summary>
    /// Telefone como o usuário digitou, com máscara e tudo. Guardado para a tela mostrar de
    /// volta o que foi cadastrado — o envio nunca usa este campo.
    /// </summary>
    [Column(TypeName = "VARCHAR(20) NOT NULL")]
    public required string Phone { get; set; }

    /// <summary>
    /// Telefone normalizado (DDI+DDD+número, só dígitos) — é ESTE que vai para o provedor e é
    /// a chave de deduplicação quando a mesma pessoa está em dois grupos. Preenchido pelo
    /// serviço a partir de <see cref="Phone"/>; nunca digitado direto.
    /// </summary>
    [Column(TypeName = "VARCHAR(20) NOT NULL")]
    public string PhoneE164 { get; set; } = string.Empty;

    /// <summary>
    /// JID resolvido junto ao provedor, quando conhecido. Reservado para o tratamento do 9º
    /// dígito: celular registrado antes de 2012 tem JID sem o 9, e adivinhar duplicaria a
    /// mensagem. Nulo enquanto não houver a consulta ao provedor.
    /// </summary>
    [Column(TypeName = "VARCHAR(40)")]
    public string? WhatsAppJid { get; set; }

    public bool Active { get; set; } = true;
}
