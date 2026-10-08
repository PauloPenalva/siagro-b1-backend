namespace SiagroB1.Reports.Dtos;

/// <summary>Linha dos relatórios de itens (saída e entrada). <see cref="Group"/> = produto + UM.</summary>
public class InvoiceItemRowDto
{
    public string Group { get; set; } = "";
    public string IssueDate { get; set; } = "";
    public string InternalNumber { get; set; } = "";
    public string DocumentNumber { get; set; } = "";
    public string Partner { get; set; } = "";
    public string Cfop { get; set; } = "";
    public string Usage { get; set; } = "";
    public decimal Quantity { get; set; }
    public string UnitOfMeasure { get; set; } = "";
    public decimal UnitPrice { get; set; }
    public decimal Total { get; set; }
    public decimal Icms { get; set; }
    public decimal Pis { get; set; }
    public decimal Cofins { get; set; }
    public decimal IbsCbs { get; set; }
    public decimal GrandTotal { get; set; }
    public string Contract { get; set; } = "";
}
