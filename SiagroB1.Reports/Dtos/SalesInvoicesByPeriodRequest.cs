using SiagroB1.Domain.Enums;

namespace SiagroB1.Reports.Dtos;

/// <summary>Filtros de "Notas de Saída por Período". Tipo vazio = Normal e Devolução.</summary>
public class SalesInvoicesByPeriodRequest : InvoiceReportRequest
{
    public SalesInvoiceType? InvoiceType { get; set; }
}
