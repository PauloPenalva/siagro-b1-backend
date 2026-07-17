namespace SiagroB1.Domain.Dtos;

public class PurchaseContractRecalcResultDto
{
    public Guid Key { get; set; }
    public string? Code { get; set; }
    public decimal PreviousAllocatedVolume { get; set; }
    public decimal NewAllocatedVolume { get; set; }
    public decimal PreviousAvaiableVolume { get; set; }
    public decimal NewAvaiableVolume { get; set; }
    public bool Changed { get; set; }
}
