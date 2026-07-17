namespace SiagroB1.Domain.Dtos;

public class PurchaseContractRecalcAllResultDto
{
    public int Scanned { get; set; }
    public int Changed { get; set; }
    public ICollection<PurchaseContractRecalcResultDto> Changes { get; set; } = [];
}
