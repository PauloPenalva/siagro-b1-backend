using Microsoft.AspNetCore.Mvc;
using SiagroB1.Reports.Services;

namespace SiagroB1.Reports.Controllers;

[ApiController]
[Route("/reports")]
public class SalesInvoicesController(IFastReportService reportService) : ControllerBase
{
    [HttpGet("SalesInvoices")]
    public async Task<IActionResult> SalesInvoices()
    {
        var parameters = new Dictionary<string, object>
        {
            ["COMPANY_LOGO"] = "logo.png"
        };

        var pdf = await reportService.GeneratePdfAsync("SalesInvoices.frx", parameters);

        return File(pdf, "application/pdf", "sales-invoices.pdf");
    }
}