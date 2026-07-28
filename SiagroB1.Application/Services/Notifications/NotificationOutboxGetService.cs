using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Notifications;

/// <summary>
/// Leitura da outbox para a tela de log de notificações.
/// </summary>
public class NotificationOutboxGetService(AppDbContext context)
{
    /// <summary>
    /// Mais recente primeiro.
    ///
    /// A ordenação daqui é só o padrão de quem consumir o endpoint direto: com paginação, o
    /// <c>[EnableQuery]</c> reordena por chave e sobrepõe este <c>OrderByDescending</c>. Quem
    /// garante a ordem na tela é o <c>sorter</c> do binding, que manda <c>$orderby</c> explícito.
    /// </summary>
    public IQueryable<NotificationOutboxMessage> QueryAll() =>
        context.NotificationOutboxMessages
            .AsNoTracking()
            .Include(m => m.Deliveries)
            .OrderByDescending(m => m.OccurredAt);

    public async Task<NotificationOutboxMessage?> GetByIdAsync(Guid key) =>
        await context.NotificationOutboxMessages
            .AsNoTracking()
            .Include(m => m.Deliveries)
            .FirstOrDefaultAsync(m => m.Key == key);
}
