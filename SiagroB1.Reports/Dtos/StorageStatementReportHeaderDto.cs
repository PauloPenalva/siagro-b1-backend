namespace SiagroB1.Reports.Dtos;

public class StorageStatementReportHeaderDto
{
    public string ReportTitle { get; set; } = "";
    public string Period { get; set; } = "";

    public decimal TotalEntrada { get; set; }
    public decimal TotalSaida { get; set; }
    public decimal TotalServicos { get; set; }
}