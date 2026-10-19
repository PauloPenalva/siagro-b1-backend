namespace SiagroB1.Domain.Entities;

/// <summary>
/// Códigos dos campos registrados em FINANCIAL_DOCUMENT_CHANGE_LOGS. São CÓDIGOS, não rótulos:
/// a tela traduz por formatter. No molde de ContractChangeLogFields.
/// </summary>
public static class FinancialDocumentChangeLogFields
{
    public const string DueDate = "DueDate";
    public const string Comments = "Comments";

    /// <summary>Campos do título lançado à mão (FinancialDocumentsManualUpdateService).</summary>
    public const string Partner = "Partner";
    public const string DocumentDate = "DocumentDate";
    public const string Branch = "Branch";
    public const string Currency = "Currency";
    public const string PaymentTerms = "PaymentTerms";
    public const string TaxDocumentNumber = "TaxDocumentNumber";

    /// <summary>Revínculo do adiantamento: os valores gravados são os CÓDIGOS dos contratos.</summary>
    public const string Contract = "Contract";

    /// <summary>Valor do provisório ajustado no lugar pelo washout (valores formatados N2 pt-BR).</summary>
    public const string NetAmount = "NetAmount";

    /// <summary>
    /// Composição no provisório, registrada nos DOIS títulos: valores "código do outro título: valor N2 pt-BR"
    /// (assinado). OldValue nulo = composto; NewValue nulo = desfeito.
    /// </summary>
    public const string Composition = "Composition";

    /// <summary>
    /// Compensação de adiantamento no provisório (spec 2026-10-14), registrada nos DOIS títulos: "código do outro título:
    /// valor N2 pt-BR". OldValue nulo = compensado; NewValue nulo = estornado.
    /// </summary>
    public const string AdvanceApplication = "AdvanceApplication";

    /// <summary>Transferência do crédito no revínculo desmembrado (spec 2026-10-14, regra 8), nos dois adiantamentos.</summary>
    public const string AdvanceTransfer = "AdvanceTransfer";
}
