using System.Text.Json;
using SiagroB1.Domain.Dtos.Notifications;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Notifications;

/// <summary>
/// Porta ÚNICA de escrita da notificação de contrato.
///
/// Assim como <see cref="PurchaseContracts.PurchaseContractsChangeLogService"/>, este serviço
/// só enfileira a linha no ChangeTracker — quem salva é o serviço de mutação que o chamou. É
/// isso que faz a notificação nascer e morrer junto com a transação do contrato: operação
/// revertida não notifica ninguém, operação commitada nunca perde o evento.
///
/// Por consequência, <c>Register</c> tem de ser chamado ANTES do <c>SaveChangesAsync</c> que
/// persiste a mutação, e nunca depois.
/// </summary>
public class ContractNotificationOutboxService(
    AppDbContext context,
    ContractNotificationPayloadBuilder payloadBuilder)
{
    public void Register(
        PurchaseContract contract,
        NotificationEventType eventType,
        string userName,
        IReadOnlyList<ContractNotificationFieldChange>? changes = null) =>
        Enqueue(
            NotificationDocumentType.PurchaseContract,
            eventType,
            contract.Key,
            contract.Code,
            userName,
            payloadBuilder.Build(contract, eventType, userName, changes));

    public void Register(
        SalesContract contract,
        NotificationEventType eventType,
        string userName,
        IReadOnlyList<ContractNotificationFieldChange>? changes = null) =>
        Enqueue(
            NotificationDocumentType.SalesContract,
            eventType,
            contract.Key,
            contract.Code,
            userName,
            payloadBuilder.Build(contract, eventType, userName, changes));

    public void RegisterPriceFixation(
        PurchaseContract contract,
        PurchaseContractPriceFixation fixation,
        NotificationEventType eventType,
        string userName)
    {
        var payload = payloadBuilder.Build(contract, eventType, userName);
        ApplyFixation(payload, fixation.FixationVolume, fixation.FixationPrice, fixation.Status);

        Enqueue(
            NotificationDocumentType.PurchaseContract, eventType,
            contract.Key, contract.Code, userName, payload);
    }

    public void RegisterPriceFixation(
        SalesContract contract,
        SalesContractPriceFixation fixation,
        NotificationEventType eventType,
        string userName)
    {
        var payload = payloadBuilder.Build(contract, eventType, userName);
        ApplyFixation(payload, fixation.FixationVolume, fixation.FixationPrice, fixation.Status);

        Enqueue(
            NotificationDocumentType.SalesContract, eventType,
            contract.Key, contract.Code, userName, payload);
    }

    private static void ApplyFixation(
        ContractNotificationPayload payload,
        decimal volume,
        decimal price,
        PriceFixationStatus? status)
    {
        payload.FixationVolume = volume;
        payload.FixationPrice = price;
        payload.FixationStatusLabel = status.HasValue
            ? NotificationEventLabels.PriceFixationStatus(status.Value)
            : null;
    }

    private void Enqueue(
        NotificationDocumentType documentType,
        NotificationEventType eventType,
        Guid documentKey,
        string? documentCode,
        string userName,
        ContractNotificationPayload payload) =>
        context.NotificationOutboxMessages.Add(new NotificationOutboxMessage
        {
            DocumentType = documentType,
            EventType = eventType,
            DocumentKey = documentKey,
            DocumentCode = documentCode,
            OccurredAt = payload.OccurredAt,
            TriggeredBy = userName,
            PayloadJson = JsonSerializer.Serialize(payload),
            Status = NotificationOutboxStatus.Pending,
            CreatedBy = userName,
        });
}
