namespace SiagroB1.Domain.Dtos;

public class SalesShipmentReleaseRecalcResultDto
{
    public Guid Key { get; set; }
    public decimal PreviousShippedQuantity { get; set; }
    public decimal NewShippedQuantity { get; set; }
    public decimal PreviousAvailableQuantity { get; set; }
    public decimal NewAvailableQuantity { get; set; }
    public bool Changed { get; set; }
}
