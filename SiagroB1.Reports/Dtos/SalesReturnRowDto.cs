namespace SiagroB1.Reports.Dtos;

public class SalesReturnRowDto
{
    public string Customer { get; set; } = "";
    public string Date { get; set; } = "";
    public string Source { get; set; } = "";
    public string InternalNumber { get; set; } = "";
    public string DocumentNumber { get; set; } = "";
    /// <summary>"NF/Série de dd/MM/yyyy" da nota original, ou "—" sem vínculo.</summary>
    public string Origin { get; set; } = "";
    public string Product { get; set; } = "";
    public decimal Quantity { get; set; }
    public string UnitOfMeasure { get; set; } = "";
    public decimal Value { get; set; }
    public string Status { get; set; } = "";

    internal DateTime SortDate { get; set; }
}
