// SiagroB1.Reports/Controllers/RoyaltyPendingController.cs
using Microsoft.AspNetCore.Mvc;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Helpers;
using SiagroB1.Reports.Services;

namespace SiagroB1.Reports.Controllers;

[ApiController]
[Route("/reports/RoyaltyPending")]
public class RoyaltyPendingController(RoyaltyPendingReportService service) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Report([FromBody] RoyaltyPendingRequest request)
    {
        if (ReportText.ValidatePeriod(request.FromDate, request.ToDate, "Informe o período de entrada.") is { } error)
            return BadRequest(error);

        var pdf = await service.ExecuteAsync(request);

        Response.Headers.ContentDisposition = "inline; filename=\"royalties-its-pendentes.pdf\"";
        return File(pdf, "application/pdf");
    }
}
