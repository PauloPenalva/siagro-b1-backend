namespace SiagroB1.Reports.Dtos;

public class SalesInvoicesByPeriodRowDto
{
    public string Branch { get; set; } = "";
    public string InternalNumber { get; set; } = "";
    public string DocumentNumber { get; set; } = "";
    public string IssueDate { get; set; } = "";
    public string Customer { get; set; } = "";
    public string Type { get; set; } = "";
    public string Status { get; set; } = "";
    public string NfeStatus { get; set; } = "";
    public decimal NetWeight { get; set; }
    public decimal ProductsTotal { get; set; }
    public decimal Freight { get; set; }
    public decimal Discount { get; set; }
    public decimal Taxes { get; set; }
    public decimal IbsCbs { get; set; }
    public decimal GrandTotal { get; set; }
}
