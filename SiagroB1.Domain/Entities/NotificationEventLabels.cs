using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Rótulos em pt-BR usados na mensagem de WhatsApp.
///
/// Ficam no backend, e não num formatter do frontend como o resto do projeto, porque o destino
/// é o celular do colaborador: não existe tela para traduzir nada depois. Espelha o papel de
/// <see cref="ContractChangeLogFields"/>, que também grava texto já traduzido.
/// </summary>
public static class NotificationEventLabels
{
    public static string DocumentType(NotificationDocumentType documentType) => documentType switch
    {
        NotificationDocumentType.PurchaseContract => "Contrato de Compra",
        NotificationDocumentType.SalesContract => "Contrato de Venda",
        _ => documentType.ToString(),
    };

    public static string Event(NotificationEventType eventType) => eventType switch
    {
        NotificationEventType.Created => "Incluído",
        NotificationEventType.HeaderUpdated => "Alterado",
        NotificationEventType.SentForApproval => "Enviado para aprovação",
        NotificationEventType.Approved => "Aprovado",
        NotificationEventType.Rejected => "Rejeitado",
        NotificationEventType.Canceled => "Cancelado",
        NotificationEventType.Closed => "Encerrado",
        NotificationEventType.Reopened => "Reaberto",
        NotificationEventType.ApprovalWithdrawn => "Aprovação retirada",
        NotificationEventType.PriceFixationCreated => "Fixação de preço incluída",
        NotificationEventType.PriceFixationApproved => "Fixação de preço aprovada",
        NotificationEventType.PriceFixationRejected => "Fixação de preço rejeitada",
        NotificationEventType.PriceFixationReversed => "Fixação de preço estornada",
        _ => eventType.ToString(),
    };

    /// <summary>
    /// Marcador visual do evento. Verde = entrou/avançou, amarelo = mudou, vermelho = parou,
    /// azul = fixação de preço. Serve para o colaborador triar a mensagem sem lê-la inteira.
    /// </summary>
    public static string EventIcon(NotificationEventType eventType) => eventType switch
    {
        NotificationEventType.Created => "🟢",
        NotificationEventType.Approved => "🟢",
        NotificationEventType.Reopened => "🟢",
        NotificationEventType.HeaderUpdated => "🟡",
        NotificationEventType.SentForApproval => "🟡",
        NotificationEventType.ApprovalWithdrawn => "🟡",
        NotificationEventType.Rejected => "🔴",
        NotificationEventType.Canceled => "🔴",
        NotificationEventType.Closed => "⚫",
        _ => "🔵",
    };

    public static string ContractStatus(ContractStatus status) => status switch
    {
        Enums.ContractStatus.Draft => "Rascunho",
        Enums.ContractStatus.InApproval => "Em aprovação",
        Enums.ContractStatus.Approved => "Aprovado",
        Enums.ContractStatus.Finished => "Encerrado",
        Enums.ContractStatus.Canceled => "Cancelado",
        Enums.ContractStatus.Rejected => "Rejeitado",
        _ => status.ToString(),
    };

    public static string PriceFixationStatus(PriceFixationStatus status) => status switch
    {
        Enums.PriceFixationStatus.InApproval => "Em aprovação",
        Enums.PriceFixationStatus.Confirmed => "Confirmada",
        Enums.PriceFixationStatus.Canceled => "Cancelada",
        Enums.PriceFixationStatus.Rejected => "Rejeitada",
        _ => status.ToString(),
    };

    /// <summary>
    /// Rótulo do campo alterado na lista "de → para".
    ///
    /// O parceiro muda de nome conforme o documento (fornecedor na compra, cliente na venda) —
    /// por isso o tipo de documento entra aqui em vez de um dicionário único.
    /// </summary>
    public static string Field(NotificationDocumentType documentType, string property) => property switch
    {
        "CardName" => documentType == NotificationDocumentType.PurchaseContract ? "Fornecedor" : "Cliente",
        "CardFName" => "Nome fantasia",
        "CardTaxId" => "CNPJ",
        "ItemName" => "Produto",
        "AgentName" => "Representante",
        "DeliveryLocationName" => "Local de entrega",
        "Complement" => "Complemento",
        "Type" => "Tipo de contrato",
        "MarketType" => "Tipo de mercado",
        "TotalVolume" => "Volume total",
        "Volume" => "Volume",
        "StandardPrice" => "Preço",
        "Price" => "Preço",
        "StandardCurrency" => "Moeda",
        "StandardCashFlowDate" => "Data de fluxo de caixa",
        "UnitOfMeasureCode" => "Unidade de medida",
        "HarvestSeasonCode" => "Safra",
        "DeliveryStartDate" => "Início da entrega",
        "DeliveryEndDate" => "Fim da entrega",
        "FreightTerms" => "Condição de frete",
        "FreightCostStandard" => "Custo do frete",
        "FreightUmCode" => "UM do frete",
        "PaymentTerms" => "Condição de pagamento",
        "Comments" => "Observação",
        "LogisticRegionCode" => "Região logística",
        "BranchCode" => "Filial",
        "TechnologyType" => "Tecnologia",
        "FunruralType" => "Funrural",
        _ => property,
    };
}
