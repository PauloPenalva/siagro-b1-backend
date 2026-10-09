namespace SiagroB1.Reports.Dtos;

public class PurchaseInvoicesByPeriodRowDto
{
    public string Branch { get; set; } = "";
    public string InternalNumber { get; set; } = "";
    public string DocumentNumber { get; set; } = "";
    public string IssueDate { get; set; } = "";
    public string PostingDate { get; set; } = "";
    public string Issuer { get; set; } = "";
    public string Type { get; set; } = "";
    public string IssuerType { get; set; } = "";
    public string Status { get; set; } = "";
    public string NfeStatus { get; set; } = "";
    public decimal DeclaredValue { get; set; }
    public decimal ProductsTotal { get; set; }
    public decimal Freight { get; set; }
    public decimal Discount { get; set; }
    public decimal Taxes { get; set; }
    public decimal GrandTotal { get; set; }
}
