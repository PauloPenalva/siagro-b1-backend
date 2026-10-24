namespace SiagroB1.Domain.Enums;

/// <summary>Situação da fatura de frete.</summary>
public enum FreightInvoiceStatus
{
    Draft = 0,
    Issued = 1,
    Settled = 2,
    Cancelled = 3,
}
