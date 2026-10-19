using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.NfeEmail;

public interface INfeEmailOutboxService
{
    /// <summary>
    /// Enfileira o e-mail automático (SEM salvar: entra no mesmo save da NF-e). Só em STANDALONE, com
    /// <c>EMAIL_SETTINGS.AutoSendEnabled</c>, e se ainda não houver linha automática para o documento e evento.
    /// Nunca lança: qualquer falha é logada e o fluxo fiscal segue.
    /// </summary>
    Task AddAutomaticAsync(NfeEmailDocumentType type, Guid documentKey, NfeEmailEventType evt, string userName);

    /// <summary>Reenvio manual para os destinatários informados. Salva.</summary>
    Task<NfeEmailMessage> AddManualAsync(
        NfeEmailDocumentType type, Guid documentKey, NfeEmailEventType evt, IReadOnlyList<string> recipients, string userName);
}

/// <summary>Fila (outbox) do e-mail da NF-e. O envio é de outro serviço; aqui só nascem as linhas.</summary>
public class NfeEmailOutboxService(IUnitOfWork db, IConfiguration configuration, ILogger<NfeEmailOutboxService> logger)
    : INfeEmailOutboxService
{
    public async Task AddAutomaticAsync(NfeEmailDocumentType type, Guid documentKey, NfeEmailEventType evt, string userName)
    {
        if (!ErpMode.IsStandalone(configuration))
            return;

        try
        {
            var enabled = await db.Context.EmailSettings.AsNoTracking()
                .Where(x => x.Key == 1).Select(x => (bool?)x.AutoSendEnabled).FirstOrDefaultAsync();

            if (enabled != true)
                return;

            bool Matches(NfeEmailMessage x) =>
                x.Trigger == NfeEmailTrigger.Automatic && x.DocumentType == type && x.DocumentKey == documentKey &&
                x.EventType == evt;

            if (db.Context.NfeEmailMessages.Local.Any(Matches))
                return;

            var exists = await db.Context.NfeEmailMessages.AsNoTracking().AnyAsync(x =>
                x.Trigger == NfeEmailTrigger.Automatic && x.DocumentType == type && x.DocumentKey == documentKey &&
                x.EventType == evt);

            if (exists)
                return;

            db.Context.NfeEmailMessages.Add(New(type, documentKey, evt, NfeEmailTrigger.Automatic, null, userName));
        }
        catch (Exception e)
        {
            // O e-mail nunca pode quebrar, atrasar ou desfazer a autorização/cancelamento.
            logger.LogError(e, "Falha ao enfileirar o e-mail da NF-e do documento {DocumentKey} ({EventType}).",
                documentKey, evt);
        }
    }

    public async Task<NfeEmailMessage> AddManualAsync(
        NfeEmailDocumentType type, Guid documentKey, NfeEmailEventType evt, IReadOnlyList<string> recipients, string userName)
    {
        var row = New(type, documentKey, evt, NfeEmailTrigger.Manual, string.Join(";", recipients), userName);
        db.Context.NfeEmailMessages.Add(row);
        await db.SaveChangesAsync();
        return row;
    }

    private static NfeEmailMessage New(
        NfeEmailDocumentType type, Guid documentKey, NfeEmailEventType evt, NfeEmailTrigger trigger, string? recipients,
        string userName) => new()
    {
        DocumentType = type,
        DocumentKey = documentKey,
        EventType = evt,
        Trigger = trigger,
        Recipients = recipients,
        Status = NfeEmailStatus.Pending,
        CreatedAt = DateTime.Now,
        CreatedBy = userName,
    };
}
