using System.Data;
using Dapper;
using SiagroB1.Reports.Dtos;

namespace SiagroB1.Reports.Services;

public class StorageDailyBalanceService(
    IWebHostEnvironment env,
    IDbConnection connection,
    IFastReportService reportService)
{
    public async Task<byte[]> ExecuteAsync(StorageDailyBalanceRequest request)
    {
        var sqlPath = Path.Combine(
            env.ContentRootPath,
            "Reports",
            "Sql",
            "StorageDailyBalance.sql"
        );

        var sql = await File.ReadAllTextAsync(sqlPath);

        var list = (List<StorageDailyBalanceResponse>) 
            await connection.QueryAsync<StorageDailyBalanceResponse>(sql, new
            {
                request.Code,
                request.FromDate,
                request.ToDate
            });
        
        var parameters = new Dictionary<string, object>
        {
            ["COMPANY_LOGO"] = "logo.png"
        };
        
        return await reportService.GeneratePdfAsync(
            "StorageDailyBalance.frx",
            list, 
            "StorageDailyBalance", 
            "StorageDailyBalance", 
            parameters);
    }
    
   
}