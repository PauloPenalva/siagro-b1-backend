namespace SiagroB1.Reports.Dtos;

public class StorageAddressReportResponse
{
    public string? BranchCode { get; set; }
    public string? Code { get; set; }
    public DateTime? CreationDate { get; set; }
    public string? Description { get; set; }

    public int OwnershipType { get; set; }
    public string? OwnershipTypeName { get; set; }

    public string? CardCode { get; set; }
    public string? CardName { get; set; }

    public string? ItemCode { get; set; }
    public string? ItemName { get; set; }

    public string? WarehouseCode { get; set; }
    public string? WarehouseName { get; set; }

    public int? Status { get; set; }
    public string? StatusName { get; set; }

    public string? UoM { get; set; }
    public string? ProcessingCostCode { get; set; }

    public decimal TotalReceipt { get; set; }
    public decimal TotalShipment { get; set; }
    public decimal TotalQualityLoss { get; set; }
    public decimal Balance { get; set; }
}