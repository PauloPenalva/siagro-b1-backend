using Microsoft.AspNetCore.Mvc;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;

namespace SiagroB1.Reports.Controllers;

/// <summary>Sem período obrigatório: o relatório é a posição no momento da emissão.</summary>
[ApiController]
[Route("/reports/ContractPosition")]
public class ContractPositionController(ContractPositionReportService service) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Report([FromBody] ContractPositionRequest request)
    {
        var pdf = await service.ExecuteAsync(request);

        Response.Headers.ContentDisposition = "inline; filename=\"contract-position.pdf\"";
        return File(pdf, "application/pdf");
    }
}
