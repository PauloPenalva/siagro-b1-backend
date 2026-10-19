namespace SiagroB1.Domain.Enums;

/// <summary>
/// Situação da fatura de armazenagem (spec 2026-10-19 parte 3), derivada dos documentos de saída que a faturam e dos
/// títulos deles (StorageInvoicesRecalculateStatusService). <see cref="Paid"/> herdou o valor 2 do antigo "Encerrada":
/// a fatura encerrada antes do faturamento por documentos já tinha sido paga.
/// </summary>
public enum StorageInvoiceStatus
{
    Open = 1,
    Paid = 2,
    Cancelled = 3,
    PartiallyInvoiced = 4,
    Invoiced = 5,
}
