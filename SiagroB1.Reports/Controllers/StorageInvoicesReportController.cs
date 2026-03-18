using Microsoft.AspNetCore.Mvc;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Reports.Interfaces;

namespace SiagroB1.Reports.Controllers;

[ApiController]
[Route("reports/StorageInvoicesReport")]
public class StorageInvoicesReportController(
    IStorageInvoiceReportService reportService,
    ILogger<StorageInvoicesReportController> logger) : ControllerBase
{
    [HttpPost("{key:guid}/print")]
    public async Task<IActionResult> Print(Guid key, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            var pdf = await reportService.GeneratePdfAsync(key, ct);

            Response.Headers.ContentDisposition = $"inline; filename=\"StorageInvoice-{key}.pdf\"";
            return File(pdf, "application/pdf");
        }
        catch (Exception e)
        { 
            if (e is BusinessException)
                return BadRequest(new { e.Message});
            
            logger.LogError(e, e.Message);
            throw;
        }
        
    }
}