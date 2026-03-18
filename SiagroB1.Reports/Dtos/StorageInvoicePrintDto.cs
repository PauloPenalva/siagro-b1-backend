namespace SiagroB1.Reports.Dtos;

public class StorageInvoicePrintDto
{
    public string InvoiceNumber { get; set; } = "";
    public string StorageAddressCode { get; set; } = "";
    public string StorageAddressDescription { get; set; } = "";
    public string CardCode { get; set; } = "";
    public string CardName { get; set; } = "";
    public string ItemCode { get; set; } = "";
    public string ItemName { get; set; } = "";
    public string WarehouseCode { get; set; } = "";
    public string WarehouseName { get; set; } = "";
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public DateTime ClosingDate { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal TotalQuantityLoss { get; set; }
    public string? Notes { get; set; }
    public List<StorageInvoicePrintItemDto> Items { get; set; } = [];
}