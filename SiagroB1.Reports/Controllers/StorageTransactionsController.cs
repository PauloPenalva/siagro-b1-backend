using Microsoft.AspNetCore.Mvc;
using SiagroB1.Reports.Services;

namespace SiagroB1.Reports.Controllers;

[ApiController]
[Route("/reports/StorageTransactions")]
public class StorageTransactionsController(StorageTransactionsService service) : ControllerBase
{
    [HttpGet("Receipts")]
    public async Task<IActionResult> GetReceipts()
    {
        var parameters = new Dictionary<string, object>
        {
            ["COMPANY_LOGO"] = "logo.png"
        };
        
        var pdf = await service.GetReceipts(parameters);
        
        Response.Headers.ContentDisposition = "inline; filename=\"StorageTransactionsReceipt.pdf\"";
        return File(pdf, "application/pdf");
    }
}