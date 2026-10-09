namespace SiagroB1.Reports.Dtos;

/// <summary>Linha de "Liberações de Venda". <see cref="Group"/> = produto + UM do contrato.</summary>
public class SalesShipmentReleaseRowDto
{
    public string Group { get; set; } = "";
    public string ReleaseDate { get; set; } = "";
    public string DeliveryDeadline { get; set; } = "";
    public string Contract { get; set; } = "";
    public string Customer { get; set; } = "";
    public string Agent { get; set; } = "";
    public string DeliveryLocation { get; set; } = "";
    public string Region { get; set; } = "";
    public string Status { get; set; } = "";
    public string UnitOfMeasure { get; set; } = "";
    public decimal ReleasedQuantity { get; set; }
    public decimal ConsumedQuantity { get; set; }
    public decimal BalanceQuantity { get; set; }
}
