namespace SiagroB1.Domain.Dtos;

public class SalesShipmentReleaseRecalcAllResultDto
{
    public int Scanned { get; set; }
    public int Changed { get; set; }
    public ICollection<SalesShipmentReleaseRecalcResultDto> Changes { get; set; } = [];
}
