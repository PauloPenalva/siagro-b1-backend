namespace SiagroB1.Reports.Dtos;

public class StorageInvoicePrintItemDto
{
    public DateTime ReferenceDate { get; set; }
    public string Description { get; set; } = "";
    public decimal Quantity { get; set; }
    public decimal UnitPriceOrRate { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal TotalQuantityLoss { get; set; }
}