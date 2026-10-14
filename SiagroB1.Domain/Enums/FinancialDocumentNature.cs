namespace SiagroB1.Domain.Enums;

public enum FinancialDocumentNature
{
    Provisional = 0,
    Firm = 1,
    Advance = 2,
    TaxWithholding = 3,

    /// <summary>
    /// Título de DEVOLUÇÃO de compra (spec 2026-10-13, títulos firmes): nasce do vínculo de uma NF de devolução com o
    /// romaneio 9/16. Compensa o firme normal (encontro de contas) ou entra NEGATIVO na composição do provisório.
    /// </summary>
    PurchaseReturn = 4
}
