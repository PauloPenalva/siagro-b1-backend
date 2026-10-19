using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Fila (outbox) do e-mail da NF-e própria autorizada/cancelada (só STANDALONE). A linha automática nasce no
/// MESMO save que grava a NF-e autorizada/cancelada; o envio é feito depois, fora do fluxo fiscal.
/// Uma linha automática por (DocumentType, DocumentKey, EventType) — índice único filtrado.
/// </summary>
[Table("NFE_EMAIL_MESSAGES")]
public class NfeEmailMessage
{
    [Key]
    public Guid Key { get; set; } = Guid.NewGuid();

    public NfeEmailDocumentType DocumentType { get; set; }

    public Guid DocumentKey { get; set; }

    public NfeEmailEventType EventType { get; set; }

    public NfeEmailTrigger Trigger { get; set; }

    /// <summary>Destinatários separados por ";" — resolvidos no envio (automático) ou informados (manual).</summary>
    [Column(TypeName = "VARCHAR(2000)")]
    public string? Recipients { get; set; }

    public NfeEmailStatus Status { get; set; } = NfeEmailStatus.Pending;

    public int AttemptCount { get; set; }

    public DateTime? LastAttemptAt { get; set; }

    [Column(TypeName = "VARCHAR(1000)")]
    public string? LastError { get; set; }

    public DateTime? SentAt { get; set; }

    public DateTime CreatedAt { get; set; }

    [Column(TypeName = "VARCHAR(100)")]
    public string CreatedBy { get; set; } = string.Empty;
}
