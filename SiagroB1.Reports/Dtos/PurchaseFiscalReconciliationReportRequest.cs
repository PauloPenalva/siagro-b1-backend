namespace SiagroB1.Reports.Dtos;

/// <summary>Filtros de "Entregas × NF do Fornecedor" — os mesmos da tela de conciliação (spec 2026-10-13 §8).</summary>
public class PurchaseFiscalReconciliationReportRequest
{
    public string? CardCode { get; set; }
    public string? ItemCode { get; set; }
    public string? ContractCode { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    /// <summary>FiscalReconciliationStatus como int; vazio = todas.</summary>
    public List<int> Statuses { get; set; } = [];
}
