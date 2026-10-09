namespace SiagroB1.Reports.Dtos;

public enum SalesReturnSource
{
    /// <summary>Devolução emitida pela empresa: documento de saída tipo Devolução.</summary>
    Own,

    /// <summary>Nota de devolução emitida pelo cliente, lançada como entrada de terceiro.</summary>
    Customer,
}

/// <summary>Filtros de "Devoluções de Venda". Origem vazia = as duas.</summary>
public class SalesReturnsRequest : InvoiceReportRequest
{
    public SalesReturnSource? Source { get; set; }
}
