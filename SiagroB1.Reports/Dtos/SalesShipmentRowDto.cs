namespace SiagroB1.Reports.Dtos;

/// <summary>Linha de "Romaneios de Venda". <see cref="Group"/> = produto + UM do romaneio.</summary>
public class SalesShipmentRowDto
{
    public string Group { get; set; } = "";
    public string TransactionDate { get; set; } = "";
    public string Code { get; set; } = "";
    public string Load { get; set; } = "";
    public string Truck { get; set; } = "";
    public string Supplier { get; set; } = "";
    public string Customers { get; set; } = "";
    public string Warehouse { get; set; } = "";
    public string InvoiceNumber { get; set; } = "";
    public string Status { get; set; } = "";
    public string UnitOfMeasure { get; set; } = "";
    public decimal GrossWeight { get; set; }
    public decimal DiscountWeight { get; set; }
    public decimal NetWeight { get; set; }
}
