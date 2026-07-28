using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Um evento de contrato pendente de notificação.
///
/// A linha é gravada na MESMA transação do contrato (o serviço de mutação só faz <c>Add</c>, o
/// caller salva) — é isso que garante que nunca se notifique uma operação que deu rollback, e
/// que nunca se perca um evento cuja operação foi commitada.
/// </summary>
[Table("NOTIFICATION_OUTBOX_MESSAGES")]
[Index(nameof(Status), nameof(OccurredAt))]
[Index(nameof(DocumentKey))]
public class NotificationOutboxMessage : BaseEntity
{
    public NotificationDocumentType DocumentType { get; set; }

    public NotificationEventType EventType { get; set; }

    /// <summary>
    /// <c>Key</c> do contrato de origem. Sem FK de propósito: o log precisa sobreviver à
    /// exclusão do contrato, senão some justamente o rastro que explica o que foi enviado.
    /// </summary>
    public Guid DocumentKey { get; set; }

    [Column(TypeName = "VARCHAR(50)")]
    public string? DocumentCode { get; set; }

    public DateTime OccurredAt { get; set; } = DateTime.Now;

    [Column(TypeName = "VARCHAR(100)")]
    public string? TriggeredBy { get; set; }

    /// <summary>
    /// Snapshot serializado do contrato NO MOMENTO DO EVENTO.
    ///
    /// A mensagem é montada a partir daqui, e não de uma releitura do contrato, por dois
    /// motivos: o envio acontece até um minuto depois (e o contrato pode ter mudado de novo no
    /// meio), e assim a renderização vira função pura, testável sem banco.
    /// </summary>
    public required string PayloadJson { get; set; }

    public NotificationOutboxStatus Status { get; set; } = NotificationOutboxStatus.Pending;

    public int AttemptCount { get; set; }

    public DateTime? LastAttemptAt { get; set; }

    /// <summary>
    /// Resumo do último erro, para a tela de log. Nunca recebe a URL do provedor: o token da
    /// instância vai no path da URL e esta tela é visível a qualquer administrador.
    /// </summary>
    [Column(TypeName = "VARCHAR(1000)")]
    public string? LastError { get; set; }

    public virtual ICollection<NotificationDeliveryLog> Deliveries { get; set; } = [];
}
