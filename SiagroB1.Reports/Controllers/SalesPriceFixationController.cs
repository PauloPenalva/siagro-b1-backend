using Microsoft.AspNetCore.Mvc;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Reports.Services;

namespace SiagroB1.Reports.Controllers;

[ApiController]
[Route("/reports/SalesPriceFixation")]
public class SalesPriceFixationController(SalesPriceFixationReportService service) : ControllerBase
{
    [HttpPost("{key:guid}/print")]
    public async Task<IActionResult> Report(Guid key)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            var pdf = await service.GeneratePdfAsync(key);

            Response.Headers.ContentDisposition = $"inline; filename=\"EspelhoFixacaoVenda-{key}.pdf\"";
            return File(pdf, "application/pdf");
        }
        catch (NotFoundException e)
        {
            return NotFound(e.Message);
        }
        catch (BusinessException e)
        {
            return BadRequest(e.Message);
        }
    }
}
