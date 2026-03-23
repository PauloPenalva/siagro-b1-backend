using SiagroB1.Domain.Enums;

namespace SiagroB1.Reports.Dtos;

public class StorageAddressReportRequest
{
    public string? BranchCode { get; set; }
    public string? CodeFrom { get; set; }
    public string? CodeTo { get; set; }

    public DateTime? CreationDateFrom { get; set; }
    public DateTime? CreationDateTo { get; set; }

    public StorageOwnershipType? OwnershipType { get; set; }
    public StorageAddressStatus? Status { get; set; }

    public string? CardCode { get; set; }
    
    public string? ItemCode { get; set; }
    
    public string? WarehouseCode { get; set; }
    
    public string? ProcessingCostCode { get; set; }

    public bool OnlyWithBalance { get; set; }
}