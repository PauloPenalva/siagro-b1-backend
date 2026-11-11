using Microsoft.AspNetCore.Mvc;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Reports.Services;

namespace SiagroB1.Reports.Controllers;

/// <summary>Posição do Lote de Armazenagem (GAC-1198): o papel enviado ao produtor que pede o saldo.</summary>
[ApiController]
[Route("/reports/StorageLotStatement")]
public class StorageLotStatementController(StorageLotStatementReportService service) : ControllerBase
{
    [HttpPost("{code}/print")]
    public async Task<IActionResult> Print(string code, CancellationToken ct)
    {
        try
        {
            var pdf = await service.ExecuteAsync(code, ct);
            Response.Headers.ContentDisposition = $"inline; filename=\"PosicaoLote-{Uri.EscapeDataString(code)}.pdf\"";
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
