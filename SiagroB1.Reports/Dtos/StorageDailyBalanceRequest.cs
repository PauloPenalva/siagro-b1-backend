namespace SiagroB1.Reports.Dtos;

public class StorageDailyBalanceRequest
{
    public required string Code { get; set; }
    
    public DateTime FromDate { get; set; }
    
    public DateTime ToDate { get; set; }
}