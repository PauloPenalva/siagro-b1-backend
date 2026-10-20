using Microsoft.AspNetCore.Mvc;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;

namespace SiagroB1.Reports.Controllers;

[ApiController]
[Route("/reports/FiscalInventory")]
public class FiscalInventoryController(FiscalInventoryService service, IConfiguration configuration) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Get([FromBody] FiscalInventoryRequest request)
    {
        if (!ErpMode.IsStandalone(configuration))
            return BadRequest(FiscalConferenceController.StandaloneOnlyMessage);

        if (string.IsNullOrWhiteSpace(request.BranchCode) || request.Date.Year is < 2000 or > 9999)
            return BadRequest("Informe a filial e a data do inventário.");

        return Ok(await service.BuildAsync(request));
    }

    [HttpPost("Book")]
    public async Task<IActionResult> Book([FromBody] FiscalInventoryBookRequest request)
    {
        if (!ErpMode.IsStandalone(configuration))
            return BadRequest(FiscalConferenceController.StandaloneOnlyMessage);

        if (string.IsNullOrWhiteSpace(request.BranchCode) || request.Date.Year is < 2000 or > 9999)
            return BadRequest("Informe a filial e a data do inventário.");

        try
        {
            var pdf = await service.BookAsync(request);
            Response.Headers.ContentDisposition = "inline; filename=\"registro-de-inventario.pdf\"";
            return File(pdf, "application/pdf");
        }
        catch (BusinessException e)
        {
            return BadRequest(e.Message);
        }
    }
}
