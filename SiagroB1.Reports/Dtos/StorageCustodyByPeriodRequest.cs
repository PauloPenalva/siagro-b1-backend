namespace SiagroB1.Reports.Dtos;

/// <summary>Situação fiscal do romaneio: Completa = toda etapa esperada tem nota ativa.</summary>
public enum StorageCustodySituationFilter
{
    All = 0,
    Pending = 1,
    Complete = 2,
}

/// <summary>Filtros de "Custódia de Armazenagem" (período sobre a data do romaneio). Filial/depositante/produto/lote do LOTE.</summary>
public class StorageCustodyByPeriodRequest : LogisticsReportRequest
{
    public string? CardCode { get; set; }

    public string? StorageAddressCode { get; set; }

    public StorageCustodySituationFilter Situation { get; set; } = StorageCustodySituationFilter.All;
}
