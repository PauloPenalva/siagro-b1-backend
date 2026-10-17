namespace SiagroB1.Domain.Enums;

public enum FinancialDocumentOrigin
{
    Manual = 0,
    PurchaseContractPriceFixation = 1,
    SalesContractPriceFixation = 2,
    PurchaseInvoice = 3,
    SalesInvoice = 4,
    FinancialDocument = 5,

    /// <summary>Título a receber do produtor gerado pela aprovação de um washout de compra.</summary>
    PurchaseContractWashout = 6,

    /// <summary>Firme/devolução nascido de um vínculo PURCHASE_FISCAL_RECONCILIATIONS (OriginKey = chave do vínculo).</summary>
    PurchaseFiscalReconciliation = 7,

    /// <summary>Título a receber do cliente gerado pela aprovação de um washout de venda (spec 2026-10-15).</summary>
    SalesContractWashout = 8,

    /// <summary>
    /// Parcelas a pagar e títulos de retenção do documento de entrada de OUTRA COMPRA (spec 2026-10-17 §3). OriginKey = chave do
    /// documento, repetida em N títulos: por isso o índice FirmOrigin exclui esta origem. Fora de toda regra de firme de contrato.
    /// </summary>
    OtherPurchaseInvoice = 9
}
