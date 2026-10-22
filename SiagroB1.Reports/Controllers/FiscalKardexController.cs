using Microsoft.AspNetCore.Mvc;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;

namespace SiagroB1.Reports.Controllers;

[ApiController]
[Route("/reports/FiscalKardex")]
public class FiscalKardexController(FiscalKardexService service, IConfiguration configuration) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Get([FromBody] FiscalKardexRequest request)
    {
        if (!ErpMode.IsStandalone(configuration))
            return BadRequest(FiscalConferenceController.StandaloneOnlyMessage);

        if (string.IsNullOrWhiteSpace(request.BranchCode)
            || request.FromDate.Date > request.ToDate.Date
            || request.FromDate.Year is < 2000 or > 9999
            || request.ToDate.Year is < 2000 or > 9999)
            return BadRequest("Informe a filial e um período válido.");

        return Ok(await service.BuildAsync(request));
    }

    /// <summary>PDF do livro Registro Kardex — modelo 3, com termos e folhas numeradas.</summary>
    [HttpPost("Book")]
    public async Task<IActionResult> Book([FromBody] FiscalKardexBookRequest request, [FromServices] FiscalKardexBookService book)
    {
        if (!ErpMode.IsStandalone(configuration))
            return BadRequest(FiscalConferenceController.StandaloneOnlyMessage);

        if (string.IsNullOrWhiteSpace(request.BranchCode)
            || request.FromDate.Date > request.ToDate.Date
            || request.FromDate.Year is < 2000 or > 9999
            || request.ToDate.Year is < 2000 or > 9999)
            return BadRequest("Informe a filial e um período válido.");

        try
        {
            var pdf = await book.BookAsync(request);
            Response.Headers.ContentDisposition = "inline; filename=\"registro-kardex.pdf\"";
            return File(pdf, "application/pdf");
        }
        catch (BusinessException e)
        {
            return BadRequest(e.Message);
        }
    }
}
