using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Infra;
using SiagroB1.Reports.Services;

namespace SiagroB1.Reports.Controllers;

[ApiController]
[Route("/reports")]
public class PurchaseContractsController(
    IFastReportService reportService,
    IUnitOfWork db) : ControllerBase
{
    [HttpGet("PurchaseContracts")]
    public async Task<IActionResult> Get()
    {
        var parameters = new Dictionary<string, object>
        {
            ["COMPANY_LOGO"] = "logo.png"
        };


        var purchaseContracts = await db.Context.PurchaseContracts
            .AsNoTracking()
            .Include(x => x.Allocations)
            .ToListAsync();
        
        var pdf = await reportService.GeneratePdfAsync(
            "PurchaseContracts.frx",
            purchaseContracts, 
            "PurchaseContracts", 
            "PurchaseContracts", 
            parameters);
        
        Response.Headers.ContentDisposition = "inline; filename=\"purchase-contracts.pdf\"";
        return File(pdf, "application/pdf");
    }
}