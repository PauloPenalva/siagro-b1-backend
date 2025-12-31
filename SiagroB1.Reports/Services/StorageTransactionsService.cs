using System.Data;
using Dapper;
using SiagroB1.Reports.Dtos;

namespace SiagroB1.Reports.Services;

public class StorageTransactionsService(
    IWebHostEnvironment env,
    IDbConnection connection,
    IFastReportService reportService)
{
    public async Task<byte[]> GetReceipts(Dictionary<string, object> parameters)
    {
        var sqlPath = Path.Combine(
            env.ContentRootPath,
            "Reports",
            "Sql",
            "StorageTransactionsReceipt.sql"
        );

        var sql = await System.IO.File.ReadAllTextAsync(sqlPath);

        var list = (List<StorageTransactionsReceiptsDto>) await connection.QueryAsync<StorageTransactionsReceiptsDto>(sql);
        
        return await reportService.GeneratePdfAsync(
            "StorageTransactionsReceipt.frx",
            list, 
            "StorageTransactions", 
            "StorageTransactions", 
            parameters);
    }
}