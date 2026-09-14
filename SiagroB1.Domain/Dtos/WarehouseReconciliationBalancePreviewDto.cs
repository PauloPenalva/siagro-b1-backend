namespace SiagroB1.Domain.Dtos;

public class WarehouseReconciliationBalancePreviewDto
{
    public decimal SystemBalance { get; set; }
    public bool IsOwnWarehouse { get; set; }
    public DateTime? LastApprovedReferenceDate { get; set; }
    public bool HasOpenReconciliation { get; set; }
}
