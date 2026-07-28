using Hangfire;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces.Notifications;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Notifications;

/// <summary>
/// Reenvio manual de uma notificação, disparado pela tela de log.
///
/// Aqui o <c>Enqueue</c> direto é seguro — diferente dos serviços de mutação, não há transação
/// de negócio pendurada que o worker pudesse ler antes do COMMIT.
/// </summary>
public class NotificationOutboxResendService(
    AppDbContext context,
    IBackgroundJobClient backgroundJobClient)
{
    public async Task ExecuteAsync(Guid outboxKey, string userName)
    {
        var message = await context.NotificationOutboxMessages
                          .FirstOrDefaultAsync(m => m.Key == outboxKey)
                      ?? throw new NotFoundException("Notificação não encontrada.");

        if (message.Status == NotificationOutboxStatus.Pending)
            throw new ApplicationException("Esta notificação já está na fila de envio.");

        // Volta para Pending para que o job a processe. As linhas de log da tentativa anterior
        // ficam: o reenvio grava novas linhas com Attempt incrementado, sem sobrescrever nada.
        message.Status = NotificationOutboxStatus.Pending;
        message.UpdatedAt = DateTime.Now;
        message.UpdatedBy = userName;

        await context.SaveChangesAsync();

        backgroundJobClient.Enqueue<IContractNotificationDispatchJob>(
            job => job.ExecuteAsync(outboxKey, CancellationToken.None));
    }
}
