namespace SiagroB1.Domain.Enums;

/// <summary>
/// Situação de uma linha da outbox de notificação. A linha nasce <see cref="Pending"/> dentro
/// da mesma transação do contrato e é resolvida depois pelo job de envio.
/// </summary>
public enum NotificationOutboxStatus
{
    /// <summary>Ainda não processada. É o único estado que o job de envio aceita processar.</summary>
    Pending = 0,

    /// <summary>Entregue a todos os destinatários.</summary>
    Sent = 1,

    /// <summary>Entregue a parte dos destinatários; o restante falhou de forma permanente.</summary>
    PartiallySent = 2,

    /// <summary>Nenhum destinatário recebeu.</summary>
    Failed = 3,

    /// <summary>
    /// Nenhum grupo ativo assinava este par documento/evento. Não é erro — evita que a tela de
    /// log mostre como falha algo que nunca teve destinatário.
    /// </summary>
    Skipped = 4,
}
