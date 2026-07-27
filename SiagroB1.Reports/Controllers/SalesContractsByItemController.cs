using Microsoft.AspNetCore.Mvc;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;

namespace SiagroB1.Reports.Controllers;

[ApiController]
[Route("/reports/SalesContractsByItem")]
public class SalesContractsByItemController(
    SalesContractsByItemReportService service) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Report([FromBody] SalesContractsByItemRequest request)
    {
        if (request.FromDate == default || request.ToDate == default)
            return BadRequest("Informe o período de emissão.");

        if (request.ToDate.Date < request.FromDate.Date)
            return BadRequest("A data final da emissão não pode ser anterior à inicial.");

        var pdf = await service.ExecuteAsync(request);

        Response.Headers.ContentDisposition = "inline; filename=\"sales-contracts-by-item.pdf\"";
        return File(pdf, "application/pdf");
    }
}
