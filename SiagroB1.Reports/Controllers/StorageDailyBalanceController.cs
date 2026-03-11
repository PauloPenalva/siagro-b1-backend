using Microsoft.AspNetCore.Mvc;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;

namespace SiagroB1.Reports.Controllers;

[ApiController]
[Route("/reports/StorageDailyBalance")]
public class StorageDailyBalanceController(StorageDailyBalanceService service) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Report([FromBody] StorageDailyBalanceRequest  request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }
        
        var pdf = await service.ExecuteAsync(request);
        
        Response.Headers.ContentDisposition = "inline; filename=\"StorageDailyBalance.pdf\"";
        return File(pdf, "application/pdf");
    }
}