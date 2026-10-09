namespace SiagroB1.Reports.Dtos;

/// <summary>Linha de "Cargas por Período". <see cref="Group"/> = produto + UM.</summary>
public class ShipmentLoadRowDto
{
    public string Group { get; set; } = "";
    public string Code { get; set; } = "";
    public string LoadDate { get; set; } = "";
    public string Type { get; set; } = "";
    public string Status { get; set; } = "";
    public string Truck { get; set; } = "";
    public string Carrier { get; set; } = "";
    public string Warehouse { get; set; } = "";
    public string Customers { get; set; } = "";
    public string UnitOfMeasure { get; set; } = "";
    public decimal TotalQuantity { get; set; }
    public decimal InvoicedQuantity { get; set; }
    public decimal ReturnedQuantity { get; set; }
    public decimal TransshippedQuantity { get; set; }
    public decimal DischargedQuantity { get; set; }
    public decimal BalanceQuantity { get; set; }
    public decimal FreightValue { get; set; }
}
