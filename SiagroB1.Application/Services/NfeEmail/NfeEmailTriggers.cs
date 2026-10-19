using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Services.NfeEmail;

/// <summary>Gatilho dos handlers da NF-e: enfileira sem nunca deixar a falha chegar ao fluxo fiscal.</summary>
internal static class NfeEmailTriggers
{
    public static async Task EnqueueAsync(
        INfeEmailOutboxService? outbox, ILogger logger, bool sendsEmail, NfeEmailDocumentType type, Guid documentKey,
        NfeEmailEventType evt, string userName)
    {
        if (outbox is null || !sendsEmail)
            return;

        try
        {
            await outbox.AddAutomaticAsync(type, documentKey, evt, userName);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Falha ao enfileirar o e-mail da NF-e do documento {DocumentKey} ({EventType}).",
                documentKey, evt);
        }
    }
}
