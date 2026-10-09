using SiagroB1.Domain.Enums;

namespace SiagroB1.Reports.Dtos;

/// <summary>Filtros de "Itens dos Documentos de Saída". Produto filtra a LINHA.</summary>
public class SalesInvoiceItemsRequest : InvoiceReportRequest
{
    public SalesInvoiceType? InvoiceType { get; set; }

    /// <summary>Código do contrato de venda (SalesContract.Code).</summary>
    public string? ContractCode { get; set; }

    public string? Cfop { get; set; }
}
