using Microsoft.AspNetCore.Mvc;
using SiagroB1.Reports.Services;

namespace SiagroB1.Reports.Controllers;

[ApiController]
[Route("/reports/WeighingTicket")]
public class WeighingTicketPrintDataController(WeighingTicketPrintDataService service) : ControllerBase
{
    [HttpPost("{key:guid}/print")]
    public async Task<IActionResult> Report(Guid key)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }
        
        var pdf = await service.GeneratePdfAsync(key);
        
        Response.Headers.ContentDisposition = $"inline; filename=\"WeighingTicket-{key}.pdf\"";
        return File(pdf, "application/pdf");
    }
}