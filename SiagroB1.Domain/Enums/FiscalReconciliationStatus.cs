namespace SiagroB1.Domain.Enums;

/// <summary>Situação fiscal do romaneio de compra (spec 2026-10-13 §4.1). Calculada, nunca persistida.</summary>
public enum FiscalReconciliationStatus
{
    WithoutInvoice = 0, // Sem NF
    Partial = 1,        // Parcial
    Reconciled = 2,     // Conciliado
}
