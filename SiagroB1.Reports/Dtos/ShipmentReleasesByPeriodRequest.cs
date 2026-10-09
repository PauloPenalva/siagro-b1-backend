using SiagroB1.Domain.Enums;

namespace SiagroB1.Reports.Dtos;

/// <summary>
/// Filtros de "Liberações de Compra" (período sobre <c>ReleaseDate</c>). Situação vazia = todas
/// menos Cancelado; Origem vazia = todas. Contrato, fornecedor e produto são do contrato de compra;
/// o armazém de retirada é o <c>DeliveryLocationCode</c> da própria liberação.
/// </summary>
public class ShipmentReleasesByPeriodRequest : LogisticsReportRequest
{
    public List<ReleaseStatus>? Statuses { get; set; }

    /// <summary>Código do contrato de compra (PurchaseContract.Code).</summary>
    public string? ContractCode { get; set; }

    public string? CardCode { get; set; }

    public string? DeliveryLocationCode { get; set; }

    public ReleaseOrigin? Origin { get; set; }
}
