using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Dtos;

/// <summary>Recorte da Conciliação Fiscal de Compras (spec 2026-10-13 D5): fornecedor e produto obrigatórios.</summary>
public class FiscalReconciliationFilter
{
    public string CardCode { get; set; } = "";
    public string ItemCode { get; set; } = "";
    public string? ContractCode { get; set; }
    public DateTime? DateFrom { get; set; }
    /// <summary>Inclusivo até o fim do dia.</summary>
    public DateTime? DateTo { get; set; }
    /// <summary>Emitente das NFs; vazio = fornecedor + parceiros com o mesmo CPF/raiz de CNPJ (D6).</summary>
    public string? IssuerCardCode { get; set; }
    public bool IncludeFullyLinked { get; set; }
}

public class FiscalReconciliationLinkDto
{
    public Guid Key { get; set; }
    public Guid PurchaseInvoiceItemKey { get; set; }
    public Guid PurchaseInvoiceKey { get; set; }
    public Guid StorageTransactionKey { get; set; }
    public string TaxDocument { get; set; } = "";
    public string? CardCode { get; set; }
    public string? CardName { get; set; }
    public DateTime? IssueDate { get; set; }
    public decimal Quantity { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
}

public class FiscalReconciliationTransactionDto
{
    public Guid Key { get; set; }
    public string? Code { get; set; }
    public DateTime? TransactionDate { get; set; }
    public StorageTransactionType TransactionType { get; set; }
    public string TransactionTypeName { get; set; } = "";
    public string CardCode { get; set; } = "";
    public string? CardName { get; set; }
    public string ItemCode { get; set; } = "";
    public string? TruckCode { get; set; }
    public string UnitOfMeasureCode { get; set; } = "";
    public List<string> ContractCodes { get; set; } = [];
    public string ContractCodesText => string.Join(", ", ContractCodes);
    public decimal PhysicalQuantity { get; set; }
    public decimal FiscalQuantity { get; set; }
    public decimal Difference => FiscalQuantity - PhysicalQuantity;
    public FiscalReconciliationStatus Status { get; set; }
    public string StatusName { get; set; } = "";
    public DateTime? FiscalReconciledAt { get; set; }
    public string? FiscalReconciledBy { get; set; }
    public List<FiscalReconciliationLinkDto> Links { get; set; } = [];
    public string InvoiceNumbersText => string.Join(", ", Links.Select(l => l.TaxDocument).Distinct());
}

public class FiscalReconciliationInvoiceLineDto
{
    public Guid PurchaseInvoiceItemKey { get; set; }
    public Guid PurchaseInvoiceKey { get; set; }
    public PurchaseInvoiceType InvoiceType { get; set; }
    public string TaxDocument { get; set; } = "";
    public DateTime? IssueDate { get; set; }
    public string CardCode { get; set; } = "";
    public string? CardName { get; set; }
    /// <summary>Emitente é o fornecedor ou tem o mesmo CPF/raiz de CNPJ. False = aviso na tela (D6).</summary>
    public bool IssuerMatchesSupplier { get; set; }
    public string? UsageName { get; set; }
    public string? ContractCode { get; set; }
    public string? UnitOfMeasureCode { get; set; }
    public decimal Quantity { get; set; }
    /// <summary>Null quando a UM da linha não converte para KG (sem fator no Complemento do Produto).</summary>
    public decimal? QuantityKg { get; set; }
    public decimal LinkedQuantity { get; set; }
    public decimal? AvailableQuantity => QuantityKg - LinkedQuantity;
}

/// <summary>Par sugerido/gravado: quanto da linha da NF vai para o romaneio (KG). <c>Quantity</c> 0 = remover.</summary>
public class FiscalReconciliationPairDto
{
    public Guid PurchaseInvoiceItemKey { get; set; }
    public Guid StorageTransactionKey { get; set; }
    public string TaxDocument { get; set; } = "";
    public string? TransactionCode { get; set; }
    public decimal CurrentQuantity { get; set; }
    public decimal Quantity { get; set; }
}
