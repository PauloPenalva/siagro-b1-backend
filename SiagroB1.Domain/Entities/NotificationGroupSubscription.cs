using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Assinatura de um <see cref="NotificationGroup"/> a um par documento/evento. Um grupo que
/// deva receber compra e venda tem duas linhas por evento.
/// </summary>
[Table("NOTIFICATION_GROUP_SUBSCRIPTIONS")]
[Index(nameof(NotificationGroupKey), nameof(DocumentType), nameof(EventType), IsUnique = true)]
[Index(nameof(DocumentType), nameof(EventType))]
public class NotificationGroupSubscription : BaseEntity
{
    public Guid NotificationGroupKey { get; set; }
    public virtual NotificationGroup? NotificationGroup { get; set; }

    public NotificationDocumentType DocumentType { get; set; }

    public NotificationEventType EventType { get; set; }
}
