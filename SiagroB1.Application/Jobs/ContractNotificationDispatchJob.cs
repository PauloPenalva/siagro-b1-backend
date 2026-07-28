using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.Notifications;
using SiagroB1.Domain.Dtos.Notifications;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Interfaces.Notifications;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Jobs;

/// <summary>
/// Envia as notificações de UMA linha da outbox.
///
/// Roda numa fila própria com um único worker: envio em rajada é o que faz um provedor
/// não-oficial banir o número da empresa.
///
/// O argumento do job é APENAS a Key da outbox — nunca o telefone ou o texto. O painel do
/// Hangfire exibe os argumentos de todo job, e ele não tem o mesmo controle de acesso das
/// telas do sistema.
/// </summary>
[Queue(QueueName)]
[AutomaticRetry(Attempts = 3, DelaysInSeconds = [30, 120, 600])]
public class ContractNotificationDispatchJob(
    AppDbContext context,
    NotificationRecipientResolver recipientResolver,
    IWhatsAppSender whatsAppSender,
    IConfiguration configuration,
    ILogger<ContractNotificationDispatchJob> logger) : IContractNotificationDispatchJob
{
    public const string QueueName = "whatsapp";

    public async Task ExecuteAsync(Guid outboxKey, CancellationToken ct = default)
    {
        var message = await context.NotificationOutboxMessages
            .FirstOrDefaultAsync(m => m.Key == outboxKey, ct);

        if (message is null)
        {
            // Pode ter sido excluída entre o enfileiramento e a execução. Sair calado é o certo.
            logger.LogDebug("Notificação {OutboxKey} não encontrada; nada a enviar.", outboxKey);
            return;
        }

        // Idempotência: o varredor roda a cada minuto e pode reenfileirar a mesma linha se um
        // envio demorar. Sem esta guarda, a mensagem sairia duplicada.
        if (message.Status != NotificationOutboxStatus.Pending)
            return;

        if (!configuration.GetValue("Notifications:WhatsApp:Enabled", true))
        {
            // Skipped, e não Pending: pendente, o varredor reenfileiraria para sempre e religar
            // a feature despejaria todo o acumulado de uma vez no WhatsApp das pessoas.
            await ResolveAsync(message, NotificationOutboxStatus.Skipped, "Envio de WhatsApp desabilitado.", ct);
            return;
        }

        var recipients = await recipientResolver.ResolveAsync(message.DocumentType, message.EventType, ct);

        if (recipients.Count == 0)
        {
            await ResolveAsync(message, NotificationOutboxStatus.Skipped, null, ct);
            return;
        }

        var payload = JsonSerializer.Deserialize<ContractNotificationPayload>(message.PayloadJson)
                      ?? throw new ApplicationException($"Payload inválido na notificação {outboxKey}.");

        var text = ContractNotificationMessageBuilder.Build(payload);
        var attempt = message.AttemptCount + 1;

        var sent = 0;
        var transientFailures = 0;
        string? firstError = null;

        foreach (var recipient in recipients)
        {
            var result = await whatsAppSender.SendTextAsync(recipient.PhoneE164, text, ct);

            if (result.Success)
                sent++;
            else
            {
                if (result.Transient) transientFailures++;
                firstError ??= result.Error;
            }

            context.NotificationDeliveryLogs.Add(new NotificationDeliveryLog
            {
                OutboxMessageKey = message.Key,
                NotificationGroupKey = recipient.GroupKey,
                GroupName = recipient.GroupName,
                RecipientName = recipient.Name,
                RecipientPhone = recipient.PhoneE164,
                Attempt = attempt,
                SentAt = DateTime.Now,
                Status = result.Success ? NotificationDeliveryStatus.Sent : NotificationDeliveryStatus.Failed,
                HttpStatusCode = result.HttpStatusCode,
                ProviderMessageId = result.ProviderMessageId,
                ErrorMessage = result.Error,
                MessageText = text,
            });
        }

        var failures = recipients.Count - sent;

        // Retentar só faz sentido quando NENHUM destinatário recebeu e toda falha foi
        // transitória: com entrega parcial, a retentativa reenviaria para quem já recebeu.
        var shouldRetry = sent == 0 && transientFailures == failures;

        message.AttemptCount = attempt;
        message.LastAttemptAt = DateTime.Now;
        message.LastError = firstError;
        message.Status = shouldRetry
            ? NotificationOutboxStatus.Pending
            : sent == recipients.Count ? NotificationOutboxStatus.Sent
            : sent > 0 ? NotificationOutboxStatus.PartiallySent
            : NotificationOutboxStatus.Failed;

        await context.SaveChangesAsync(ct);

        if (shouldRetry)
        {
            // Lançar É o pedido de retentativa ao Hangfire. O log de envio já foi gravado
            // acima, então a tentativa fica registrada mesmo que todas as retentativas falhem.
            logger.LogWarning(
                "Falha transitória ao notificar {OutboxKey} (tentativa {Attempt}): {Error}",
                outboxKey, attempt, firstError);

            throw new ApplicationException(
                $"Falha transitória ao enviar a notificação {outboxKey}: {firstError}");
        }
    }

    private async Task ResolveAsync(
        NotificationOutboxMessage message,
        NotificationOutboxStatus status,
        string? error,
        CancellationToken ct)
    {
        message.Status = status;
        message.LastError = error;
        message.LastAttemptAt = DateTime.Now;

        await context.SaveChangesAsync(ct);
    }
}
