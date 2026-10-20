using Microsoft.AspNetCore.Mvc;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;

namespace SiagroB1.Reports.Controllers;

[ApiController]
[Route("/reports/FinancialSettlements")]
public class FinancialSettlementsReportController(FinancialSettlementsReportService service) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Report([FromBody] FinancialSettlementsRequest request)
    {
        if (FinancialSettlementsReportService.Validate(request) is { } error)
            return BadRequest(error);

        try
        {
            var pdf = await service.ExecuteAsync(request);
            Response.Headers.ContentDisposition = "inline; filename=\"financial-settlements.pdf\"";
            return File(pdf, "application/pdf");
        }
        catch (ApplicationException e)
        {
            return BadRequest(e.Message);
        }
    }
}
