using Microsoft.Extensions.Configuration;
using SiagroB1.Domain.Dtos.Notifications;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Services.Notifications;

/// <summary>
/// Converte o contrato no snapshot que será gravado em <c>PayloadJson</c>.
///
/// Lê <c>Notifications:AppBaseUrl</c> direto do <see cref="IConfiguration"/>, seguindo a
/// convenção do projeto (não há <c>IOptions</c> em lugar nenhum do solution).
/// </summary>
public class ContractNotificationPayloadBuilder(IConfiguration configuration)
{
    /// <summary>Rotas do SPA. Precisam bater com os <c>pattern</c> do <c>manifest.json</c>.</summary>
    private const string PurchaseRoute = "purchase-contracts";
    private const string SalesRoute = "sales-contracts";

    public ContractNotificationPayload Build(
        PurchaseContract contract,
        NotificationEventType eventType,
        string userName,
        IReadOnlyList<ContractNotificationFieldChange>? changes = null) => new()
    {
        DocumentType = NotificationDocumentType.PurchaseContract,
        EventType = eventType,
        ContractKey = contract.Key,
        ContractCode = contract.Code,
        Complement = contract.Complement,
        CardCode = contract.CardCode,
        CardName = contract.CardName,
        ItemCode = contract.ItemCode,
        ItemName = contract.ItemName,
        TotalVolume = contract.TotalVolume,
        UnitOfMeasureCode = contract.UnitOfMeasureCode,
        Price = contract.StandardPrice,
        CurrencyCode = contract.StandardCurrency?.ToString(),
        DeliveryStartDate = contract.DeliveryStartDate,
        DeliveryEndDate = contract.DeliveryEndDate,
        HarvestSeasonCode = contract.HarvestSeasonCode,
        DeliveryLocationName = contract.DeliveryLocationName,
        BranchCode = contract.BranchCode,
        StatusLabel = contract.Status.HasValue ? NotificationEventLabels.ContractStatus(contract.Status.Value) : null,
        TriggeredBy = userName,
        OccurredAt = DateTime.Now,
        DetailUrl = DetailUrl(PurchaseRoute, contract.Key),
        FieldChanges = [.. changes ?? []],
    };

    public ContractNotificationPayload Build(
        SalesContract contract,
        NotificationEventType eventType,
        string userName,
        IReadOnlyList<ContractNotificationFieldChange>? changes = null) => new()
    {
        DocumentType = NotificationDocumentType.SalesContract,
        EventType = eventType,
        ContractKey = contract.Key,
        ContractCode = contract.Code,
        Complement = contract.Complement,
        CardCode = contract.CardCode,
        CardName = contract.CardName,
        ItemCode = contract.ItemCode,
        ItemName = contract.ItemName,
        TotalVolume = contract.TotalVolume,
        UnitOfMeasureCode = contract.UnitOfMeasureCode,
        Price = contract.Price,
        CurrencyCode = contract.StandardCurrency?.ToString(),
        DeliveryStartDate = contract.DeliveryStartDate,
        DeliveryEndDate = contract.DeliveryEndDate,
        HarvestSeasonCode = contract.HarvestSeasonCode,
        BranchCode = contract.BranchCode,
        StatusLabel = contract.Status.HasValue ? NotificationEventLabels.ContractStatus(contract.Status.Value) : null,
        TriggeredBy = userName,
        OccurredAt = DateTime.Now,
        DetailUrl = DetailUrl(SalesRoute, contract.Key),
        FieldChanges = [.. changes ?? []],
    };

    /// <summary>
    /// Sem <c>Notifications:AppBaseUrl</c> a mensagem sai sem link — melhor do que um link
    /// quebrado, que gera chamado de suporte.
    /// </summary>
    private string? DetailUrl(string route, Guid key)
    {
        var baseUrl = configuration["Notifications:AppBaseUrl"];

        return string.IsNullOrWhiteSpace(baseUrl)
            ? null
            : $"{baseUrl.TrimEnd('/')}/#/{route}/{key}/detail";
    }
}
