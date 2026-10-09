using Microsoft.AspNetCore.Mvc;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;

namespace SiagroB1.Reports.Controllers;

/// <summary>Sem período obrigatório: o relatório é a posição no momento da emissão.</summary>
[ApiController]
[Route("/reports/ContractMonthlyPosition")]
public class ContractMonthlyPositionController(ContractMonthlyPositionReportService service) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Report([FromBody] ContractMonthlyPositionRequest request)
    {
        var pdf = await service.ExecuteAsync(request);

        Response.Headers.ContentDisposition = "inline; filename=\"contract-monthly-position.pdf\"";
        return File(pdf, "application/pdf");
    }
}
