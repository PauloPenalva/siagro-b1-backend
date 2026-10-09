namespace SiagroB1.Reports.Dtos;

/// <summary>Linha de "Liberações de Compra". <see cref="Group"/> = produto + UM do contrato.</summary>
public class ShipmentReleaseRowDto
{
    public string Group { get; set; } = "";
    public string ReleaseDate { get; set; } = "";
    public string Contract { get; set; } = "";
    public string Supplier { get; set; } = "";
    public string DeliveryLocation { get; set; } = "";
    public string Origin { get; set; } = "";
    public string Status { get; set; } = "";
    public string UnitOfMeasure { get; set; } = "";
    public decimal ReleasedQuantity { get; set; }
    public decimal WithdrawnQuantity { get; set; }
    public decimal BalanceQuantity { get; set; }
}
