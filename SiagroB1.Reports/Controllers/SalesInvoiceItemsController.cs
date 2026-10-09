using Microsoft.AspNetCore.Mvc;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Helpers;
using SiagroB1.Reports.Services;

namespace SiagroB1.Reports.Controllers;

[ApiController]
[Route("/reports/SalesInvoiceItems")]
public class SalesInvoiceItemsController(SalesInvoiceItemsReportService service) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Report([FromBody] SalesInvoiceItemsRequest request)
    {
        if (InvoiceReportText.Validate(request) is { } error)
            return BadRequest(error);

        var pdf = await service.ExecuteAsync(request);

        Response.Headers.ContentDisposition = "inline; filename=\"sales-invoice-items.pdf\"";
        return File(pdf, "application/pdf");
    }
}
