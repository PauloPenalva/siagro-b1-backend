using System.Text.Json.Serialization;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Dtos;

// [JsonPropertyName] em tudo: as funções devolvem IActionResult cru, que o ASP.NET serializa em camelCase e com enum
// inteiro — a tela lê PascalCase e enum por nome (mesmo padrão de PurchaseDischargeListDto).

/// <summary>Recorte da Conciliação Fiscal de Compras (spec 2026-10-13 D5): fornecedor e produto obrigatórios.</summary>
public class FiscalReconciliationFilter
{
    [JsonPropertyName("CardCode")]
    public string CardCode { get; set; } = "";
    [JsonPropertyName("ItemCode")]
    public string ItemCode { get; set; } = "";
    [JsonPropertyName("ContractCode")]
    public string? ContractCode { get; set; }
    [JsonPropertyName("DateFrom")]
    public DateTime? DateFrom { get; set; }
    /// <summary>Inclusivo até o fim do dia.</summary>
    [JsonPropertyName("DateTo")]
    public DateTime? DateTo { get; set; }
    /// <summary>Emitente das NFs; vazio = fornecedor + parceiros com o mesmo CPF/raiz de CNPJ (D6).</summary>
    [JsonPropertyName("IssuerCardCode")]
    public string? IssuerCardCode { get; set; }
    [JsonPropertyName("IncludeFullyLinked")]
    public bool IncludeFullyLinked { get; set; }
}

public class FiscalReconciliationLinkDto
{
    [JsonPropertyName("Key")]
    public Guid Key { get; set; }
    [JsonPropertyName("PurchaseInvoiceItemKey")]
    public Guid PurchaseInvoiceItemKey { get; set; }
    [JsonPropertyName("PurchaseInvoiceKey")]
    public Guid PurchaseInvoiceKey { get; set; }
    [JsonPropertyName("StorageTransactionKey")]
    public Guid StorageTransactionKey { get; set; }
    [JsonPropertyName("TaxDocument")]
    public string TaxDocument { get; set; } = "";
    [JsonPropertyName("CardCode")]
    public string? CardCode { get; set; }
    [JsonPropertyName("CardName")]
    public string? CardName { get; set; }
    [JsonPropertyName("IssueDate")]
    public DateTime? IssueDate { get; set; }
    [JsonPropertyName("Quantity")]
    public decimal Quantity { get; set; }
    [JsonPropertyName("CreatedAt")]
    public DateTime CreatedAt { get; set; }
    [JsonPropertyName("CreatedBy")]
    public string? CreatedBy { get; set; }
    /// <summary>Título firme/devolução do vínculo (spec 2026-10-13, títulos firmes); o ativo, ou o último cancelado.</summary>
    [JsonPropertyName("FirmDocumentKey")]
    public Guid? FirmDocumentKey { get; set; }
    [JsonPropertyName("FirmDocumentCode")]
    public string? FirmDocumentCode { get; set; }
    /// <summary>"Solto", "Composto" ou "Cancelado" (PurchaseFirmPayableRules.SituationName).</summary>
    [JsonPropertyName("FirmSituation")]
    public string? FirmSituation { get; set; }
}

public class FiscalReconciliationTransactionDto
{
    [JsonPropertyName("Key")]
    public Guid Key { get; set; }
    [JsonPropertyName("Code")]
    public string? Code { get; set; }
    [JsonPropertyName("TransactionDate")]
    public DateTime? TransactionDate { get; set; }
    [JsonPropertyName("TransactionType")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public StorageTransactionType TransactionType { get; set; }
    [JsonPropertyName("TransactionTypeName")]
    public string TransactionTypeName { get; set; } = "";
    [JsonPropertyName("CardCode")]
    public string CardCode { get; set; } = "";
    [JsonPropertyName("CardName")]
    public string? CardName { get; set; }
    [JsonPropertyName("ItemCode")]
    public string ItemCode { get; set; } = "";
    [JsonPropertyName("TruckCode")]
    public string? TruckCode { get; set; }
    [JsonPropertyName("UnitOfMeasureCode")]
    public string UnitOfMeasureCode { get; set; } = "";
    [JsonPropertyName("ContractCodes")]
    public List<string> ContractCodes { get; set; } = [];
    [JsonPropertyName("ContractCodesText")]
    public string ContractCodesText => string.Join(", ", ContractCodes);
    [JsonPropertyName("PhysicalQuantity")]
    public decimal PhysicalQuantity { get; set; }
    [JsonPropertyName("FiscalQuantity")]
    public decimal FiscalQuantity { get; set; }
    [JsonPropertyName("Difference")]
    public decimal Difference => FiscalQuantity - PhysicalQuantity;
    [JsonPropertyName("Status")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public FiscalReconciliationStatus Status { get; set; }
    [JsonPropertyName("StatusName")]
    public string StatusName { get; set; } = "";
    [JsonPropertyName("FiscalReconciledAt")]
    public DateTime? FiscalReconciledAt { get; set; }
    [JsonPropertyName("FiscalReconciledBy")]
    public string? FiscalReconciledBy { get; set; }
    [JsonPropertyName("Links")]
    public List<FiscalReconciliationLinkDto> Links { get; set; } = [];
    [JsonPropertyName("InvoiceNumbersText")]
    public string InvoiceNumbersText => string.Join(", ", Links.Select(l => l.TaxDocument).Distinct());
}

public class FiscalReconciliationInvoiceLineDto
{
    [JsonPropertyName("PurchaseInvoiceItemKey")]
    public Guid PurchaseInvoiceItemKey { get; set; }
    [JsonPropertyName("PurchaseInvoiceKey")]
    public Guid PurchaseInvoiceKey { get; set; }
    [JsonPropertyName("InvoiceType")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PurchaseInvoiceType InvoiceType { get; set; }
    [JsonPropertyName("TaxDocument")]
    public string TaxDocument { get; set; } = "";
    [JsonPropertyName("IssueDate")]
    public DateTime? IssueDate { get; set; }
    [JsonPropertyName("CardCode")]
    public string CardCode { get; set; } = "";
    [JsonPropertyName("CardName")]
    public string? CardName { get; set; }
    /// <summary>Emitente é o fornecedor ou tem o mesmo CPF/raiz de CNPJ. False = aviso na tela (D6).</summary>
    [JsonPropertyName("IssuerMatchesSupplier")]
    public bool IssuerMatchesSupplier { get; set; }
    [JsonPropertyName("UsageName")]
    public string? UsageName { get; set; }
    [JsonPropertyName("ContractCode")]
    public string? ContractCode { get; set; }
    [JsonPropertyName("UnitOfMeasureCode")]
    public string? UnitOfMeasureCode { get; set; }
    [JsonPropertyName("Quantity")]
    public decimal Quantity { get; set; }
    /// <summary>Null quando a UM da linha não converte para KG (sem fator no Complemento do Produto).</summary>
    [JsonPropertyName("QuantityKg")]
    public decimal? QuantityKg { get; set; }
    [JsonPropertyName("LinkedQuantity")]
    public decimal LinkedQuantity { get; set; }
    [JsonPropertyName("AvailableQuantity")]
    public decimal? AvailableQuantity => QuantityKg - LinkedQuantity;
}

/// <summary>Par sugerido/gravado: quanto da linha da NF vai para o romaneio (KG). <c>Quantity</c> 0 = remover.</summary>
public class FiscalReconciliationPairDto
{
    [JsonPropertyName("PurchaseInvoiceItemKey")]
    public Guid PurchaseInvoiceItemKey { get; set; }
    [JsonPropertyName("StorageTransactionKey")]
    public Guid StorageTransactionKey { get; set; }
    [JsonPropertyName("TaxDocument")]
    public string TaxDocument { get; set; } = "";
    [JsonPropertyName("TransactionCode")]
    public string? TransactionCode { get; set; }
    [JsonPropertyName("CurrentQuantity")]
    public decimal CurrentQuantity { get; set; }
    [JsonPropertyName("Quantity")]
    public decimal Quantity { get; set; }
}
