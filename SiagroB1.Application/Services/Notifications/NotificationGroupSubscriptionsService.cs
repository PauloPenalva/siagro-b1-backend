using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Shared.Base;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Notifications;

/// <summary>
/// Assinaturas do grupo (quais pares documento/evento ele recebe). A duplicidade é barrada pelo
/// índice único <c>GroupKey + DocumentType + EventType</c>, no banco.
/// </summary>
public class NotificationGroupSubscriptionsService(
    AppDbContext context, ILogger<IBaseService<NotificationGroupSubscription, Guid>> logger)
    : BaseService<NotificationGroupSubscription, Guid>(context, logger)
{
    /// <summary>
    /// Persiste alterações já aplicadas a uma entidade RASTREADA (PATCH via <c>Delta</c>).
    ///
    /// O <c>UpdateAsync</c> da base não serve: faz <c>State = Modified</c> e marca também o
    /// <c>RowId</c> de <c>BaseEntity</c>, que é identity — o SQL Server recusa com
    /// "Cannot update identity column 'RowId'".
    /// </summary>
    public async Task SaveTrackedChangesAsync() => await _context.SaveChangesAsync();
}
