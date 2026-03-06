using Microsoft.AspNetCore.Mvc;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;

namespace SiagroB1.Reports.Controllers;

[ApiController]
[Route("/reports/StorageTransactions")]
public class StorageTransactionsController(StorageTransactionsService service) : ControllerBase
{
    [HttpPost("Receipts")]
    public async Task<IActionResult> ReceiptsReport([FromBody] StorageTransactionsRequest  request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }
        
        var pdf = await service.ReceiptsReport(request);
        
        Response.Headers.ContentDisposition = "inline; filename=\"StorageTransactionsReceipt.pdf\"";
        return File(pdf, "application/pdf");
    }
    
    [HttpPost("Shipments")]
    public async Task<IActionResult> ShipmentsReport([FromBody] StorageTransactionsRequest  request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }
        
        var pdf = await service.ShipmentsReport(request);
        
        Response.Headers.ContentDisposition = "inline; filename=\"StorageTransactionsShipment.pdf\"";
        return File(pdf, "application/pdf");
    }
}