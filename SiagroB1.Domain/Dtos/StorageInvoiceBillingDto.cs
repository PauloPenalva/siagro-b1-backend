using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Dtos;

/// <summary>Faturamento da fatura de armazenagem (spec 2026-10-19 parte 3): documentos de saída, títulos e saldo.</summary>
public class StorageInvoiceBillingDto
{
    public decimal TotalAmount { get; set; }
    public decimal BilledAmount { get; set; }
    public decimal Balance { get; set; }
    public List<StorageInvoiceBillingDocumentDto> Documents { get; set; } = [];
    public List<StorageInvoiceBillingReceivableDto> Receivables { get; set; } = [];
}

public class StorageInvoiceBillingDocumentDto
{
    public Guid Key { get; set; }
    public string? InvoiceNumber { get; set; }
    public TaxDocumentKind TaxDocumentKind { get; set; }
    public string? TaxDocumentNumber { get; set; }
    public string? TaxDocumentSeries { get; set; }
    public DateTime? InvoiceDate { get; set; }
    public decimal Total { get; set; }
    public InvoiceStatus? InvoiceStatus { get; set; }
}

public class StorageInvoiceBillingReceivableDto
{
    public Guid Key { get; set; }
    public string? Code { get; set; }
    public string? InvoiceNumber { get; set; }
    public int? InstallmentNumber { get; set; }
    public DateTime DueDate { get; set; }
    public decimal NetAmount { get; set; }
    public decimal OpenAmount { get; set; }
    public FinancialDocumentStatus Status { get; set; }
}
