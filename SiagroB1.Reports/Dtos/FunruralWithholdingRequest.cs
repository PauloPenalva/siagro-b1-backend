namespace SiagroB1.Reports.Dtos;

/// <summary>
/// Filtros de "Funrural Retido por Item de Entrada". O período filtra a EMISSÃO DA ENTRADA; as
/// devoluções de compra são abatidas na linha de origem qualquer que seja a data delas.
/// </summary>
public class FunruralWithholdingRequest : InvoiceReportRequest
{
    /// <summary>Código do contrato de compra (PurchaseContract.Code).</summary>
    public string? ContractCode { get; set; }

    /// <summary>
    /// Só entradas escrituráveis, como na Conferência Fiscal: fora NF-e cancelada, denegada ou
    /// inutilizada e NF-e própria ainda não autorizada. Desligado no relatório avulso.
    /// </summary>
    public bool BookableOnly { get; set; }
}
