using Microsoft.AspNetCore.Mvc;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Reports.Interfaces;

namespace SiagroB1.Reports.Controllers;

/// <summary>Impresso da fatura de frete. Só STANDALONE.</summary>
[ApiController]
[Route("reports/FreightInvoicesReport")]
public class FreightInvoicesReportController(
    IFreightInvoiceReportService reportService,
    IConfiguration configuration,
    ILogger<FreightInvoicesReportController> logger) : ControllerBase
{
    [HttpPost("{key:guid}/print")]
    public async Task<IActionResult> Print(Guid key, CancellationToken ct)
    {
        if (!ErpMode.IsStandalone(configuration)) return BadRequest(FiscalConferenceController.StandaloneOnlyMessage);

        try
        {
            var pdf = await reportService.GeneratePdfAsync(key, ct);
            Response.Headers.ContentDisposition = $"inline; filename=\"FreightInvoice-{key}.pdf\"";
            return File(pdf, "application/pdf");
        }
        catch (Exception e)
        {
            if (e is BusinessException)
                return BadRequest(new { e.Message });
            if (e is NotFoundException)
                return NotFound(new { e.Message });

            logger.LogError(e, e.Message);
            throw;
        }
    }
}
