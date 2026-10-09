using SiagroB1.Domain.Enums;

namespace SiagroB1.Reports.Dtos;

/// <summary>
/// Filtros de "Cargas por Período" (período sobre <c>LoadDate</c>). Situação vazia = todas menos
/// Cancelada; Tipo vazio = Normal e Remoção. Cliente segue a regra de
/// <see cref="Helpers.LogisticsReportText.LoadHasCustomer"/>.
/// </summary>
public class ShipmentLoadsByPeriodRequest : LogisticsReportRequest
{
    public List<ShipmentLoadStatus>? Statuses { get; set; }

    public ShipmentLoadType? LoadType { get; set; }

    public string? WarehouseCode { get; set; }

    public string? CarrierCardCode { get; set; }

    public string? TruckCode { get; set; }

    public string? CardCode { get; set; }
}
