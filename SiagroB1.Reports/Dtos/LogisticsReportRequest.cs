namespace SiagroB1.Reports.Dtos;

/// <summary>
/// Filtros comuns aos relatórios de logística de venda (cargas, liberações, romaneios). Só o
/// período é obrigatório; a data filtrada depende do relatório.
/// </summary>
public abstract class LogisticsReportRequest
{
    /// <summary>Início do período.</summary>
    public DateTime FromDate { get; set; }

    /// <summary>Fim do período, inclusivo até o fim do dia.</summary>
    public DateTime ToDate { get; set; }

    public string? BranchCode { get; set; }

    public string? ItemCode { get; set; }
}
