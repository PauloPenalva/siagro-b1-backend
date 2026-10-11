using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Dtos;

/// <summary>Romaneio candidato à reconciliação com o retorno de depósito (spec 2026-10-10 §3.5).</summary>
public class StorageReturnTransactionDto
{
    public Guid Key { get; set; }
    public string? Code { get; set; }
    public DateTime? TransactionDate { get; set; }
    public string? TruckCode { get; set; }
    public decimal NetWeight { get; set; }
    public string? UnitOfMeasureCode { get; set; }

    /// <summary>Reconciliado com ESTE retorno.</summary>
    public bool IsReconciled { get; set; }
}

/// <summary>Remetido / Retornado / A retornar / Expedido sem retorno da liberação (spec 2026-10-10 §2.5, §5.1).</summary>
public class StorageRemittanceSummaryDto
{
    public Guid? RemittanceKey { get; set; }

    /// <summary>TaxDocumentNumber ?? InvoiceNumber.</summary>
    public string? RemittanceNumber { get; set; }
    public InvoiceStatus? RemittanceStatus { get; set; }
    public NfeStatus? RemittanceNfeStatus { get; set; }
    public decimal RemittedQuantity { get; set; }
    public decimal ReturnedQuantity { get; set; }
    public decimal PendingReturnQuantity { get; set; }
    public decimal ShippedWithoutReturnQuantity { get; set; }
    public List<StorageRemittanceReturnDto> Returns { get; set; } = [];
    public List<StorageRemittanceTransactionDto> Transactions { get; set; } = [];
}

public class StorageRemittanceReturnDto
{
    public Guid PurchaseInvoiceKey { get; set; }
    public string? Number { get; set; }
    public DateTime? InvoiceDate { get; set; }
    public decimal Quantity { get; set; }
    public InvoiceStatus InvoiceStatus { get; set; }
}

public class StorageRemittanceTransactionDto
{
    public Guid Key { get; set; }
    public string? Code { get; set; }
    public DateTime? TransactionDate { get; set; }
    public string? TruckCode { get; set; }
    public decimal NetWeight { get; set; }
    public Guid? ReturnPurchaseInvoiceKey { get; set; }
    public string? ReturnNumber { get; set; }
}
