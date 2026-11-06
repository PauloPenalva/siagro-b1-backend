namespace SiagroB1.Reports.Dtos;

/// <summary>Total de um serviço na fatura de armazenagem (resumo para conferência, GAC-18).</summary>
public class StorageInvoiceServiceSummaryDto
{
    public string Service { get; set; } = "";
    public int Lines { get; set; }
    public decimal Quantity { get; set; }
    public decimal TotalQuantityLoss { get; set; }
    public decimal TotalAmount { get; set; }
}
