using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Resultado do envio para UM destinatário em UMA tentativa. Um reenvio pela tela gera linhas
/// novas, com <see cref="Attempt"/> incrementado — nada é sobrescrito.
///
/// Nome, grupo e telefone são snapshots: o log precisa continuar legível depois que o membro
/// for removido ou o grupo apagado.
/// </summary>
[Table("NOTIFICATION_DELIVERY_LOGS")]
[Index(nameof(OutboxMessageKey))]
[Index(nameof(SentAt))]
[Index(nameof(Status))]
public class NotificationDeliveryLog : BaseEntity
{
    public Guid OutboxMessageKey { get; set; }
    public virtual NotificationOutboxMessage? OutboxMessage { get; set; }

    public Guid? NotificationGroupKey { get; set; }

    [Column(TypeName = "VARCHAR(100)")]
    public string? GroupName { get; set; }

    [Column(TypeName = "VARCHAR(100)")]
    public string? RecipientName { get; set; }

    [Column(TypeName = "VARCHAR(20)")]
    public string? RecipientPhone { get; set; }

    public int Attempt { get; set; } = 1;

    public DateTime SentAt { get; set; } = DateTime.Now;

    public NotificationDeliveryStatus Status { get; set; }

    public int? HttpStatusCode { get; set; }

    /// <summary>
    /// <c>messageId</c> devolvido pelo provedor. É o que permite conferir no painel dele que a
    /// mensagem existiu de fato.
    /// </summary>
    [Column(TypeName = "VARCHAR(100)")]
    public string? ProviderMessageId { get; set; }

    /// <summary>
    /// Erro do provedor. Nunca recebe a URL da requisição — o token da instância vai no path.
    /// </summary>
    [Column(TypeName = "VARCHAR(1000)")]
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Texto exatamente como foi enviado. É o que permite responder "o que exatamente essa
    /// pessoa recebeu?" sem ter de reconstruir a mensagem a partir do contrato de hoje.
    /// </summary>
    public string? MessageText { get; set; }
}
