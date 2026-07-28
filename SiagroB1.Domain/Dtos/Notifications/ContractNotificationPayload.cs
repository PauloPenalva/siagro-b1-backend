using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Dtos.Notifications;

/// <summary>
/// Snapshot do contrato no momento do evento, serializado em
/// <c>NOTIFICATION_OUTBOX_MESSAGES.PayloadJson</c>.
///
/// É a ÚNICA entrada do montador da mensagem. Nada aqui pode depender de releitura do banco:
/// o envio acontece depois (o contrato pode ter mudado), e essa independência é o que torna a
/// renderização testável sem DbContext.
///
/// Classe com propriedades settáveis, e não record, para o <c>System.Text.Json</c>
/// desserializar sem construtor posicional.
/// </summary>
public class ContractNotificationPayload
{
    public NotificationDocumentType DocumentType { get; set; }
    public NotificationEventType EventType { get; set; }

    public Guid ContractKey { get; set; }
    public string? ContractCode { get; set; }
    public string? Complement { get; set; }

    public string? CardCode { get; set; }
    public string? CardName { get; set; }

    public string? ItemCode { get; set; }
    public string? ItemName { get; set; }

    public decimal TotalVolume { get; set; }
    public string? UnitOfMeasureCode { get; set; }

    /// <summary>
    /// Preço unitário: <c>StandardPrice</c> na compra, <c>Price</c> na venda. Vem zerado em
    /// contrato a fixar (PAF), onde o preço só nasce das fixações aprovadas.
    /// </summary>
    public decimal Price { get; set; }

    public string? CurrencyCode { get; set; }

    public DateTime? DeliveryStartDate { get; set; }
    public DateTime? DeliveryEndDate { get; set; }

    public string? HarvestSeasonCode { get; set; }
    public string? DeliveryLocationName { get; set; }
    public string? BranchCode { get; set; }

    /// <summary>Situação do contrato já em pt-BR — a mensagem não traduz nada.</summary>
    public string? StatusLabel { get; set; }

    public string? TriggeredBy { get; set; }
    public DateTime OccurredAt { get; set; }

    /// <summary>Link para a tela de detalhe. Vazio se <c>Notifications:AppBaseUrl</c> não estiver configurado.</summary>
    public string? DetailUrl { get; set; }

    /// <summary>
    /// Preenchido apenas em <see cref="NotificationEventType.HeaderUpdated"/>. Quando há
    /// mudanças, elas SUBSTITUEM o bloco de dados do contrato na mensagem.
    /// </summary>
    public List<ContractNotificationFieldChange> FieldChanges { get; set; } = [];

    /// <summary>Preenchidos apenas nos eventos de fixação de preço.</summary>
    public decimal? FixationVolume { get; set; }
    public decimal? FixationPrice { get; set; }
    public string? FixationStatusLabel { get; set; }
}

/// <summary>
/// Um campo do cabeçalho que mudou, já formatado em pt-BR. O montador da mensagem só concatena
/// — a formatação de decimal, data e enum acontece antes, ao montar o payload.
/// </summary>
public class ContractNotificationFieldChange
{
    /// <summary>Nome da propriedade da entidade, para rastreabilidade.</summary>
    public string Field { get; set; } = string.Empty;

    /// <summary>Rótulo em pt-BR exibido na mensagem.</summary>
    public string Label { get; set; } = string.Empty;

    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
}
