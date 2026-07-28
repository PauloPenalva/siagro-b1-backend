using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Services.Notifications;

/// <summary>
/// Quais colunas do cabeçalho do contrato viram linha na mensagem de alteração.
///
/// É ALLOW-LIST, não deny-list, de propósito: uma coluna nova adicionada ao contrato amanhã não
/// deve, por padrão, virar mensagem no celular de ninguém. O custo de esquecer de incluir é uma
/// alteração que não aparece; o de esquecer de excluir é spam — e a coluna esquecida mais
/// provável é justamente uma derivada, gravada em lote por serviço de recálculo.
///
/// Ficam de fora, sempre: <c>AllocatedVolume</c> e <c>FixedVolume</c> (derivadas, recalculadas
/// em lote), <c>Status</c> (tem evento próprio), os carimbos de auditoria e <c>RowVersion</c>.
///
/// Dos pares código/nome desnormalizados (<c>CardCode</c>/<c>CardName</c>,
/// <c>ItemCode</c>/<c>ItemName</c>, ...) entra apenas o NOME: é o que a pessoa lê, e os dois
/// são gravados juntos pelo <c>UpdateService</c> — incluir os dois duplicaria cada troca de
/// parceiro em duas linhas.
/// </summary>
public static class ContractNotifiableFields
{
    private static readonly HashSet<string> Shared =
    [
        "Complement",
        "Type",
        "MarketType",
        "AgentName",
        "CardName",
        "DeliveryStartDate",
        "DeliveryEndDate",
        "FreightTerms",
        "FreightCostStandard",
        "FreightUmCode",
        "ItemName",
        "UnitOfMeasureCode",
        "HarvestSeasonCode",
        "TotalVolume",
        "StandardCurrency",
        "StandardCashFlowDate",
        "PaymentTerms",
        "Comments",
        "LogisticRegionCode",
        "BranchCode",
    ];

    private static readonly HashSet<string> PurchaseOnly =
    [
        "StandardPrice",
        "DeliveryLocationName",
        "TechnologyType",
        "FunruralType",
    ];

    private static readonly HashSet<string> SalesOnly =
    [
        "Price",
        "Volume",
        "CardFName",
        "CardTaxId",
    ];

    private static readonly HashSet<string> Purchase = [.. Shared, .. PurchaseOnly];
    private static readonly HashSet<string> Sales = [.. Shared, .. SalesOnly];

    public static IReadOnlySet<string> For(NotificationDocumentType documentType) =>
        documentType == NotificationDocumentType.PurchaseContract ? Purchase : Sales;
}
