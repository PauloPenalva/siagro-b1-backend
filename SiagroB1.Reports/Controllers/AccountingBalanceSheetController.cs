using Microsoft.AspNetCore.Mvc;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;

namespace SiagroB1.Reports.Controllers;

/// <summary>Balanço pela visão gerencial: PDF no POST da raiz e as mesmas linhas em JSON em /data (o Excel é do frontend).</summary>
[ApiController]
[Route("/reports/AccountingBalanceSheet")]
public class AccountingBalanceSheetController(BalanceSheetReportService service, IConfiguration configuration)
    : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Report([FromBody] BalanceSheetReportRequest request)
    {
        if (!ErpMode.IsStandalone(configuration)) return BadRequest(FiscalConferenceController.StandaloneOnlyMessage);
        if (BalanceSheetReportService.Validate(request) is { } error) return BadRequest(error);

        try
        {
            var pdf = await service.ExecuteAsync(request);
            Response.Headers.ContentDisposition = "inline; filename=\"accounting-balance-sheet.pdf\"";
            return File(pdf, "application/pdf");
        }
        catch (ApplicationException e)
        {
            return BadRequest(e.Message);
        }
    }

    [HttpPost("data")]
    public async Task<IActionResult> Data([FromBody] BalanceSheetReportRequest request)
    {
        if (!ErpMode.IsStandalone(configuration)) return BadRequest(FiscalConferenceController.StandaloneOnlyMessage);
        if (BalanceSheetReportService.Validate(request) is { } error) return BadRequest(error);

        try { return Ok(await service.RowsAsync(request)); }
        catch (ApplicationException e) { return BadRequest(e.Message); }
    }
}
