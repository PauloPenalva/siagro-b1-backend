using System.Data;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;

namespace SiagroB1.Reports.Controllers;

[ApiController]
[Route("/reports/StorageTransactions")]
public class StorageTransactionsController(
    IFastReportService reportService,
    IWebHostEnvironment env,
    IDbConnection connection) : ControllerBase
{
    [HttpGet("Receipts")]
    public async Task<IActionResult> Get()
    {
        var parameters = new Dictionary<string, object>
        {
            ["COMPANY_LOGO"] = "logo.png"
        };
        
        var sqlPath = Path.Combine(
            env.ContentRootPath,
            "Reports",
            "Sql",
            "StorageTransactionsReceipt.sql"
        );

        var sql = await System.IO.File.ReadAllTextAsync(sqlPath);

        var list = (List<StorageTransactionsReceiptsDto>) await connection.QueryAsync<StorageTransactionsReceiptsDto>(sql);
        
        var pdf = await reportService.GeneratePdfAsync(
            "StorageTransactionsReceipt.frx",
            list, 
            "StorageTransactions", 
            "StorageTransactions", 
            parameters);
        
        Response.Headers.ContentDisposition = "inline; filename=\"StorageTransactionsReceipt.pdf\"";
        return File(pdf, "application/pdf");
    }
}