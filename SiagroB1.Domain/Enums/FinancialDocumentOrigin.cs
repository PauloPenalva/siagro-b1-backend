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
    PurchaseContractWashout = 6
}
