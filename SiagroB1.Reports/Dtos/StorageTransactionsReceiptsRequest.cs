namespace SiagroB1.Reports.Dtos;

public class StorageTransactionsReceiptsRequest
{
    public string? BranchCodeFrom { get; set; } 
    
    public string? BranchCodeTo { get; set; }

    public DateTime? TransactionDateFrom { get; set; }
    
    public DateTime? TransactionDateTo { get; set; }

    public string? CardCodeFrom { get; set; }
    
    public string? CardCodeTo { get; set; }

    public string? StorageAddressCodeFrom { get; set; }
    
    public string? StorageAddressCodeTo { get; set; }
    
    public string? ItemCodeFrom { get; set; }
    
    public string? ItemCodeTo { get; set; }

    public string? WarehouseCodeFrom { get; set; }
    
    public string? WarehouseCodeTo { get; set; }

    public string? TruckCodeFrom { get; set; }
    
    public string? TruckCodeTo { get; set; }
}