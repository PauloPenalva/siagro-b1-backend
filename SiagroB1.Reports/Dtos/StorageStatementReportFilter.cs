using SiagroB1.Domain.Enums;

namespace SiagroB1.Reports.Dtos;

public class StorageStatementReportFilter
{
    public string? BranchCode { get; set; }
    public DateTime? DateStart { get; set; }
    public DateTime? DateEnd { get; set; }

    public string? StorageAddressCode { get; set; }
    public string? CardCode { get; set; }
    public string? ItemCode { get; set; }
    public string? WarehouseCode { get; set; }

    public StorageTransactionType? TransactionType { get; set; }
    public StorageTransactionsStatus? TransactionStatus { get; set; }

    public bool OnlyConfirmed { get; set; }
    public bool IncludeCancelled { get; set; } = true;
}