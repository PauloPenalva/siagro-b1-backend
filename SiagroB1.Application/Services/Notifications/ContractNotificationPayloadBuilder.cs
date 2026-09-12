using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SiagroB1.Domain.Dtos.Notifications;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Notifications;

/// <summary>
/// Converte o contrato no snapshot que será gravado em <c>PayloadJson</c>.
///
/// Lê <c>Notifications:AppBaseUrl</c> direto do <see cref="IConfiguration"/>, seguindo a
/// convenção do projeto (não há <c>IOptions</c> em lugar nenhum do solution).
///
/// Também é aqui que entram os dados de cadastro que o contrato não carrega (nome da filial,
/// unidade comercial do produto): o snapshot os congela junto com o evento, e o montador da
/// mensagem continua sem ler banco. As leituras são síncronas porque <c>Register</c> é
/// síncrono em todos os serviços de mutação — são duas buscas por chave.
/// </summary>
public class ContractNotificationPayloadBuilder(AppDbContext context, IConfiguration configuration)
{
    /// <summary>Rotas do SPA. Precisam bater com os <c>pattern</c> do <c>manifest.json</c>.</summary>
    private const string PurchaseRoute = "purchase-contracts";
    private const string SalesRoute = "sales-contracts";

    /// <summary>Unidade física em que o fator comercial é definido ("KG por unidade comercial").</summary>
    private const string BaseUnitOfMeasure = "KG";

    public ContractNotificationPayload Build(
        PurchaseContract contract,
        NotificationEventType eventType,
        string userName,
        IReadOnlyList<ContractNotificationFieldChange>? changes = null) => Enrich(new()
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
        CreationDate = contract.CreationDate,
        PaymentForecastDate = contract.StandardCashFlowDate,
        PaymentTerms = contract.PaymentTerms,
        DeliveryStartDate = contract.DeliveryStartDate,
        DeliveryEndDate = contract.DeliveryEndDate,
        HarvestSeasonCode = contract.HarvestSeasonCode,
        DeliveryLocationName = contract.DeliveryLocationName,
        BranchCode = contract.BranchCode,
        StatusLabel = contract.Status.HasValue ? NotificationEventLabels.ContractStatus(contract.Status.Value) : null,
        TriggeredBy = userName,
        OccurredAt = DateTime.Now,
        DetailUrl = DetailUrl(PurchaseRoute, contract.Key),
    }, changes);

    public ContractNotificationPayload Build(
        SalesContract contract,
        NotificationEventType eventType,
        string userName,
        IReadOnlyList<ContractNotificationFieldChange>? changes = null) => Enrich(new()
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
        CreationDate = contract.CreationDate,
        PaymentForecastDate = contract.StandardCashFlowDate,
        PaymentTerms = contract.PaymentTerms,
        DeliveryStartDate = contract.DeliveryStartDate,
        DeliveryEndDate = contract.DeliveryEndDate,
        HarvestSeasonCode = contract.HarvestSeasonCode,
        BranchCode = contract.BranchCode,
        StatusLabel = contract.Status.HasValue ? NotificationEventLabels.ContractStatus(contract.Status.Value) : null,
        TriggeredBy = userName,
        OccurredAt = DateTime.Now,
        DetailUrl = DetailUrl(SalesRoute, contract.Key),
    }, changes);

    private ContractNotificationPayload Enrich(
        ContractNotificationPayload payload,
        IReadOnlyList<ContractNotificationFieldChange>? changes)
    {
        payload.BranchName = BranchName(payload.BranchCode);
        payload.FieldChanges = [.. (changes ?? []).Select(TranslateBranchChange)];
        ApplyCommercialUnit(payload);

        return payload;
    }

    /// <summary>
    /// Nome curto da filial, que é o que as listas de contrato exibem; cai para o nome completo.
    /// Nulo quando a filial não existe — a mensagem então mostra o código.
    /// </summary>
    private string? BranchName(string? branchCode)
    {
        if (string.IsNullOrWhiteSpace(branchCode))
            return null;

        var branch = context.Branchs
            .AsNoTracking()
            .Where(b => b.Code == branchCode)
            .Select(b => new { b.ShortName, b.BranchName })
            .FirstOrDefault();

        if (branch is null)
            return null;

        return string.IsNullOrWhiteSpace(branch.ShortName) ? branch.BranchName : branch.ShortName;
    }

    /// <summary>
    /// Troca de filial na lista de alterações: o diff entrega os códigos, a mensagem mostra os
    /// nomes. Cópia, e não mutação, para não mexer na lista que o serviço de mutação montou.
    /// </summary>
    private ContractNotificationFieldChange TranslateBranchChange(ContractNotificationFieldChange change) =>
        change.Field != nameof(PurchaseContract.BranchCode)
            ? change
            : new ContractNotificationFieldChange
            {
                Field = change.Field,
                Label = change.Label,
                OldValue = BranchName(change.OldValue) ?? change.OldValue,
                NewValue = BranchName(change.NewValue) ?? change.NewValue,
                OldNumber = change.OldNumber,
                NewNumber = change.NewNumber,
            };

    /// <summary>
    /// Mesmo cadastro e mesma regra do diálogo de faturamento: UM e fator comercial andam juntos
    /// e, se faltar um, a mensagem fica em KG. A trava extra — contrato em KG — existe porque o
    /// fator é "KG por unidade comercial": aplicado a um contrato em outra unidade daria um
    /// número errado com cara de certo.
    /// </summary>
    private void ApplyCommercialUnit(ContractNotificationPayload payload)
    {
        var itemCode = payload.ItemCode;

        if (string.IsNullOrWhiteSpace(itemCode)
            || !string.Equals(payload.UnitOfMeasureCode, BaseUnitOfMeasure, StringComparison.OrdinalIgnoreCase))
            return;

        var complement = context.ItemComplements
            .AsNoTracking()
            .Where(c => c.ItemCode == itemCode)
            .Select(c => new { c.CommercialUnitOfMeasureCode, c.CommercialFactor })
            .FirstOrDefault();

        if (complement is null
            || string.IsNullOrWhiteSpace(complement.CommercialUnitOfMeasureCode)
            || complement.CommercialFactor is not > 0
            || string.Equals(complement.CommercialUnitOfMeasureCode, payload.UnitOfMeasureCode, StringComparison.OrdinalIgnoreCase))
            return;

        payload.CommercialUnitOfMeasureCode = complement.CommercialUnitOfMeasureCode;
        payload.CommercialFactor = complement.CommercialFactor;
    }

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
