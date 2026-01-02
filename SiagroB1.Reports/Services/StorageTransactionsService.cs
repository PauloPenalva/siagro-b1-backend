using System.Data;
using Dapper;
using SiagroB1.Reports.Dtos;

namespace SiagroB1.Reports.Services;

public class StorageTransactionsService(
    IWebHostEnvironment env,
    IDbConnection connection,
    IFastReportService reportService)
{
    public async Task<byte[]> ReceiptsReport(StorageTransactionsReceiptsRequest request)
    {
        var sqlPath = Path.Combine(
            env.ContentRootPath,
            "Reports",
            "Sql",
            "StorageTransactionsReceipt.sql"
        );

        var sql = await File.ReadAllTextAsync(sqlPath);

        var list = (List<StorageTransactionsReceiptsResponse>) 
            await connection.QueryAsync<StorageTransactionsReceiptsResponse>(sql, new
            {
                request.BranchCodeFrom,
                request.BranchCodeTo,
                request.TransactionDateFrom,
                request.TransactionDateTo,
                request.CardCodeFrom,
                request.CardCodeTo,
                request.StorageAddressCodeFrom,
                request.StorageAddressCodeTo,
                request.ItemCodeFrom,
                request.ItemCodeTo,
                request.WarehouseCodeFrom,
                request.WarehouseCodeTo,
                request.TruckCodeFrom,
                request.TruckCodeTo
            });
        
        var parameters = new Dictionary<string, object>
        {
            ["COMPANY_LOGO"] = "logo.png"
        };
        
        return await reportService.GeneratePdfAsync(
            "StorageTransactionsReceipt.frx",
            list, 
            "StorageTransactions", 
            "StorageTransactions", 
            parameters);
    }
}