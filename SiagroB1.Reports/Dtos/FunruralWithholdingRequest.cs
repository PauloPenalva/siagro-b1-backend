namespace SiagroB1.Reports.Dtos;

/// <summary>
/// Filtros de "Funrural Retido por Item de Entrada". O período filtra a EMISSÃO DA ENTRADA; as
/// devoluções de compra são abatidas na linha de origem qualquer que seja a data delas.
/// </summary>
public class FunruralWithholdingRequest : InvoiceReportRequest
{
    /// <summary>Código do contrato de compra (PurchaseContract.Code).</summary>
    public string? ContractCode { get; set; }

}
