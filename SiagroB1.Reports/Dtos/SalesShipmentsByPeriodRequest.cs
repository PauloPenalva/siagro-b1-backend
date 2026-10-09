using SiagroB1.Domain.Enums;

namespace SiagroB1.Reports.Dtos;

/// <summary>
/// Filtros de "Romaneios de Venda" (SalesShipment, período sobre <c>TransactionDate</c>). Situação
/// vazia = todas menos Cancelado. <see cref="HasLoad"/>: true = só com carga, false = só sem
/// carga, null = ambos. Cliente segue a regra da carga; romaneio sem carga nunca casa.
/// </summary>
public class SalesShipmentsByPeriodRequest : LogisticsReportRequest
{
    public List<StorageTransactionsStatus>? Statuses { get; set; }

    public string? WarehouseCode { get; set; }

    public string? TruckCode { get; set; }

    public string? CardCode { get; set; }

    public bool? HasLoad { get; set; }
}
