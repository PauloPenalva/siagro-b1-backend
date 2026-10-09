using Microsoft.AspNetCore.Mvc;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Helpers;
using SiagroB1.Reports.Services;

namespace SiagroB1.Reports.Controllers;

[ApiController]
[Route("/reports/SalesInvoicesByPeriod")]
public class SalesInvoicesByPeriodController(SalesInvoicesByPeriodReportService service) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Report([FromBody] SalesInvoicesByPeriodRequest request)
    {
        if (InvoiceReportText.Validate(request) is { } error)
            return BadRequest(error);

        var pdf = await service.ExecuteAsync(request);

        Response.Headers.ContentDisposition = "inline; filename=\"sales-invoices-by-period.pdf\"";
        return File(pdf, "application/pdf");
    }
}
