using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

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

    /// <summary>
    /// O save que grava a NF-e autorizada/cancelada. Se ele falhar com a linha de e-mail pendente (ex.: índice único
    /// violado por outro gravador), a linha é descartada e o save é refeito SEM ela: a NF-e sempre vai para o banco.
    /// Sem linha de e-mail no rastreador, a falha é do fluxo fiscal e sobe como antes.
    /// </summary>
    public static async Task SaveFiscalAsync(IUnitOfWork db, ILogger logger)
    {
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException e)
        {
            var emailEntries = db.Context.ChangeTracker.Entries<NfeEmailMessage>()
                .Where(x => x.State == EntityState.Added).ToList();

            if (emailEntries.Count == 0)
                throw;

            logger.LogError(e, "Falha ao gravar a fila de e-mail da NF-e; a NF-e é gravada sem o e-mail.");
            foreach (var entry in emailEntries)
                entry.State = EntityState.Detached;

            await db.SaveChangesAsync();
        }
    }
}
