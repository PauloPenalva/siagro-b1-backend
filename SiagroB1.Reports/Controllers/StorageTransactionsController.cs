using Microsoft.AspNetCore.Mvc;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;

namespace SiagroB1.Reports.Controllers;

[ApiController]
[Route("/reports/StorageTransactions")]
public class StorageTransactionsController(StorageTransactionsService service) : ControllerBase
{
    [HttpPost("Receipts")]
    public async Task<IActionResult> ReceiptsReport([FromBody] StorageTransactionsReceiptsRequest  request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }
        
        var pdf = await service.ReceiptsReport(request);
        
        Response.Headers.ContentDisposition = "inline; filename=\"StorageTransactionsReceipt.pdf\"";
        return File(pdf, "application/pdf");
    }
}