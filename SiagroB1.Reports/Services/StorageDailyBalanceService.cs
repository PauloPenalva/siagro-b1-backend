using System.Data;
using Dapper;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;

namespace SiagroB1.Reports.Services;

public class StorageDailyBalanceService(
    IWebHostEnvironment env,
    IDbConnection connection,
    IFastReportService reportService,
    IUnitOfWork db)
{
    public async Task<byte[]> ExecuteAsync(StorageDailyBalanceRequest request)
    {
        var address = await db.Context.StorageAddresses
            .FirstOrDefaultAsync(x => x.Code == request.Code);
        
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
                StorageAddressCode = request.Code,
                DateFrom = request.FromDate,
                DateTo = request.ToDate
            });
        
        var parameters = new Dictionary<string, object>
        {
            ["COMPANY_LOGO"] = "logo.png",
            ["StorageAddressCode"] = request.Code,
            ["FromDate"] = request.FromDate,
            ["ToDate"] = request.ToDate,
            ["Item"] = address?.ItemName + $" ({address?.ItemCode})", 
            ["Customer"] = address?.CardName + $" ({address?.CardCode})",
        };
        
        return await reportService.GeneratePdfAsync(
            "StorageDailyBalance.frx",
            list, 
            "StorageDailyBalance", 
            "StorageDailyBalance", 
            parameters);
    }
    
   
}