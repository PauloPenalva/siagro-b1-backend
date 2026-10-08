using SiagroB1.Domain.Enums;

namespace SiagroB1.Reports.Dtos;

/// <summary>Filtros de "Notas de Entrada por Período". Tipo e Emissão vazios = todos.</summary>
public class PurchaseInvoicesByPeriodRequest : InvoiceReportRequest
{
    public PurchaseInvoiceType? InvoiceType { get; set; }

    public DocumentIssuerType? IssuerType { get; set; }
}
