using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Notifications;

/// <summary>
/// Leitura do log de envio por destinatário.
/// </summary>
public class NotificationDeliveryLogsGetService(AppDbContext context)
{
    public IQueryable<NotificationDeliveryLog> QueryAll() =>
        context.NotificationDeliveryLogs
            .AsNoTracking()
            .OrderByDescending(log => log.SentAt);
}
