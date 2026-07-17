namespace SiagroB1.Domain.Dtos;

public class ShipmentReleaseRecalcAllResultDto
{
    public int Scanned { get; set; }
    public int Changed { get; set; }
    public ICollection<ShipmentReleaseRecalcResultDto> Changes { get; set; } = [];
}
