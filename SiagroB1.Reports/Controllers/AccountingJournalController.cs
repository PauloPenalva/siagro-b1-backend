using Microsoft.AspNetCore.Mvc;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;

namespace SiagroB1.Reports.Controllers;

/// <summary>Diário: PDF no POST da raiz e as mesmas linhas em JSON em /data (o Excel é do frontend).</summary>
[ApiController]
[Route("/reports/AccountingJournal")]
public class AccountingJournalController(JournalReportService service, IConfiguration configuration)
    : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Report([FromBody] JournalReportRequest request)
    {
        if (!ErpMode.IsStandalone(configuration)) return BadRequest(FiscalConferenceController.StandaloneOnlyMessage);
        if (JournalReportService.Validate(request) is { } error) return BadRequest(error);

        try
        {
            var pdf = await service.ExecuteAsync(request);
            Response.Headers.ContentDisposition = "inline; filename=\"accounting-journal.pdf\"";
            return File(pdf, "application/pdf");
        }
        catch (ApplicationException e)
        {
            return BadRequest(e.Message);
        }
    }

    [HttpPost("data")]
    public async Task<IActionResult> Data([FromBody] JournalReportRequest request)
    {
        if (!ErpMode.IsStandalone(configuration)) return BadRequest(FiscalConferenceController.StandaloneOnlyMessage);
        if (JournalReportService.Validate(request) is { } error) return BadRequest(error);

        try { return Ok(await service.RowsAsync(request)); }
        catch (ApplicationException e) { return BadRequest(e.Message); }
    }
}
