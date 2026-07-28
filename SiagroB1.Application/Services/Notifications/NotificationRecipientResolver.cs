using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Notifications;

/// <param name="GroupKey">Grupo que motivou o envio. Registrado no log para explicar POR QUE a pessoa recebeu.</param>
public record NotificationRecipient(Guid GroupKey, string GroupName, string Name, string PhoneE164);

/// <summary>
/// Quem recebe um determinado par documento/evento: membros ativos de grupos ativos que
/// assinam esse par.
/// </summary>
public class NotificationRecipientResolver(AppDbContext context)
{
    public async Task<IReadOnlyList<NotificationRecipient>> ResolveAsync(
        NotificationDocumentType documentType,
        NotificationEventType eventType,
        CancellationToken ct = default)
    {
        var candidates = await (
            from subscription in context.NotificationGroupSubscriptions
            join notificationGroup in context.NotificationGroups
                on subscription.NotificationGroupKey equals notificationGroup.Key
            join member in context.NotificationGroupMembers
                on notificationGroup.Key equals member.NotificationGroupKey
            where subscription.DocumentType == documentType
                  && subscription.EventType == eventType
                  && notificationGroup.Active
                  && member.Active
                  && member.PhoneE164 != ""
            select new NotificationRecipient(
                notificationGroup.Key, notificationGroup.Name, member.Name, member.PhoneE164))
            .ToListAsync(ct);

        // Dedupe por telefone: quem está em dois grupos que assinam o mesmo evento recebe uma
        // mensagem só. Duas seriam ruído, e num provedor não-oficial ruído multiplicado é risco
        // de banimento do número da empresa.
        return [.. candidates
            .GroupBy(recipient => recipient.PhoneE164)
            .Select(sameNumber => sameNumber.First())];
    }
}
