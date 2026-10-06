using Microsoft.AspNetCore.Mvc;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Reports.Services;

namespace SiagroB1.Reports.Controllers;

[ApiController]
[Route("/reports/Danfe")]
public class DanfeController(DanfeReportService service) : ControllerBase
{
    [HttpPost("{key:guid}/print")]
    public async Task<IActionResult> Report(Guid key) =>
        await ExecuteReportAsync(() => service.GeneratePdfAsync(key));

    [HttpPost("purchase-invoices/{key:guid}/print")]
    public async Task<IActionResult> PurchaseReport(Guid key) =>
        await ExecuteReportAsync(() => service.GeneratePurchasePdfAsync(key));

    [HttpPost("{key:guid}/cce/{sequence:int}/print")]
    public async Task<IActionResult> CorrectionReport(Guid key, int sequence) =>
        await ExecuteReportAsync(() => service.GenerateCorrectionPdfAsync(key, sequence));

    [HttpPost("purchase-invoices/{key:guid}/cce/{sequence:int}/print")]
    public async Task<IActionResult> PurchaseCorrectionReport(Guid key, int sequence) =>
        await ExecuteReportAsync(() => service.GeneratePurchaseCorrectionPdfAsync(key, sequence));

    private async Task<IActionResult> ExecuteReportAsync(Func<Task<(byte[], string)>> generateReport)
    {
        try
        {
            var (pdf, fileName) = await generateReport();

            Response.Headers.ContentDisposition = $"inline; filename=\"{fileName}\"";
            return File(pdf, "application/pdf");
        }
        catch (NotFoundException e)
        {
            return NotFound(e.Message);
        }
        catch (DefaultException e)
        {
            return BadRequest(e.Message);
        }
    }
}
