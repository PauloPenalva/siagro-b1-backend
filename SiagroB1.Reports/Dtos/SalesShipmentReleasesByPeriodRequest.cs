using SiagroB1.Domain.Enums;

namespace SiagroB1.Reports.Dtos;

/// <summary>
/// Filtros de "Liberações de Venda" (período sobre <c>ReleaseDate</c>). Situação vazia = todas
/// menos Cancelado. Contrato, cliente, vendedor, produto e região são do contrato de venda.
/// </summary>
public class SalesShipmentReleasesByPeriodRequest : LogisticsReportRequest
{
    public List<ReleaseStatus>? Statuses { get; set; }

    /// <summary>Código do contrato de venda (SalesContract.Code).</summary>
    public string? ContractCode { get; set; }

    public string? CardCode { get; set; }

    public int? AgentCode { get; set; }

    public string? LogisticRegionCode { get; set; }
}
