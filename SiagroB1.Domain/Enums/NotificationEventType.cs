namespace SiagroB1.Domain.Enums;

/// <summary>
/// O que aconteceu com o contrato. Gravado em <c>NOTIFICATION_OUTBOX_MESSAGES.EventType</c> e
/// assinado por grupo em <c>NOTIFICATION_GROUP_SUBSCRIPTIONS.EventType</c>.
///
/// Os valores são contrato com o banco e com o frontend (o formatter traduz cada um para o
/// rótulo em pt-BR) — não renumere depois que houver linhas gravadas. A faixa 10+ é reservada
/// aos eventos de fixação de preço, para caber evento novo de contrato sem embaralhar.
/// </summary>
public enum NotificationEventType
{
    Created = 0,

    /// <summary>
    /// Edição do cabeçalho. Só ocorre em contrato em rascunho: os dois <c>UpdateService</c>
    /// barram qualquer outro status.
    /// </summary>
    HeaderUpdated = 1,

    SentForApproval = 2,
    Approved = 3,
    Rejected = 4,
    Canceled = 5,
    Closed = 6,
    Reopened = 7,
    ApprovalWithdrawn = 8,

    PriceFixationCreated = 10,
    PriceFixationApproved = 11,
    PriceFixationRejected = 12,

    /// <summary>
    /// Estorno de fixação confirmada. Nome intencionalmente diferente de "Canceled": o serviço
    /// chama-se <c>PriceFixationsCancelService</c>, mas a fixação volta para
    /// <see cref="PriceFixationStatus.InApproval"/>, não para cancelada.
    /// </summary>
    PriceFixationReversed = 13,
}
