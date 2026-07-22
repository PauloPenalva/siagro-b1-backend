namespace SiagroB1.Domain.Dtos;

public class SalesContractRecalcAllResultDto
{
    public int Scanned { get; set; }
    public int Changed { get; set; }
    public ICollection<SalesContractRecalcResultDto> Changes { get; set; } = [];
}
