namespace SiagroB1.Domain.Interfaces.Notifications;

/// <summary>
/// Envia as notificações de UMA linha da outbox. Enfileirado pelo varredor (e pelo reenvio
/// manual da tela de log).
/// </summary>
public interface IContractNotificationDispatchJob
{
    Task ExecuteAsync(Guid outboxKey, CancellationToken ct = default);
}

/// <summary>
/// Varre a outbox e enfileira o envio das linhas pendentes.
///
/// É o mecanismo de entrega, não uma rede de segurança: enfileirar direto no serviço de
/// mutação não funcionaria, porque o Hangfire grava em conexão própria com autocommit e o
/// worker poderia ler a outbox antes do COMMIT da transação de negócio, não achar a linha e
/// sair em silêncio.
/// </summary>
public interface IContractNotificationSweepJob
{
    Task ExecuteAsync(CancellationToken ct = default);
}
