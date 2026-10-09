using Microsoft.AspNetCore.Mvc;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Helpers;
using SiagroB1.Reports.Services;

namespace SiagroB1.Reports.Controllers;

[ApiController]
[Route("/reports/SalesReturns")]
public class SalesReturnsController(SalesReturnsReportService service) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Report([FromBody] SalesReturnsRequest request)
    {
        if (InvoiceReportText.Validate(request) is { } error)
            return BadRequest(error);

        var pdf = await service.ExecuteAsync(request);

        Response.Headers.ContentDisposition = "inline; filename=\"sales-returns.pdf\"";
        return File(pdf, "application/pdf");
    }
}
