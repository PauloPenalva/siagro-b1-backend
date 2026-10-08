using SiagroB1.Domain.Enums;

namespace SiagroB1.Reports.Dtos;

/// <summary>Filtros de "Itens dos Documentos de Entrada". Produto filtra a LINHA.</summary>
public class PurchaseInvoiceItemsRequest : InvoiceReportRequest
{
    public PurchaseInvoiceType? InvoiceType { get; set; }

    public DocumentIssuerType? IssuerType { get; set; }

    /// <summary>Código do contrato de compra (PurchaseContract.Code).</summary>
    public string? ContractCode { get; set; }

    public string? Cfop { get; set; }
}
