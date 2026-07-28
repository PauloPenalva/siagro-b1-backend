using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Grupo de colaboradores que recebe notificações por WhatsApp.
///
/// Cadastro próprio, deliberadamente independente de <c>USERS</c>: quem precisa ser avisado de
/// um contrato nem sempre é usuário do sistema, e <c>USERS</c> vive em outro banco
/// (<c>SiagroCommon</c>), o que impediria o join numa consulta só.
/// </summary>
[Table("NOTIFICATION_GROUPS")]
[Index(nameof(Code), IsUnique = true)]
public class NotificationGroup : BaseEntity
{
    [Column(TypeName = "VARCHAR(20) NOT NULL")]
    public required string Code { get; set; }

    [Column(TypeName = "VARCHAR(100) NOT NULL")]
    public required string Name { get; set; }

    /// <summary>
    /// Grupo inativo não recebe nada, mesmo com assinaturas cadastradas. É o desligamento
    /// granular — o <c>Notifications:WhatsApp:Enabled</c> desliga tudo de uma vez.
    /// </summary>
    public bool Active { get; set; } = true;

    public virtual ICollection<NotificationGroupMember> Members { get; set; } = [];

    public virtual ICollection<NotificationGroupSubscription> Subscriptions { get; set; } = [];
}
