using Microsoft.AspNetCore.Mvc;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Helpers;
using SiagroB1.Reports.Services;

namespace SiagroB1.Reports.Controllers;

[ApiController]
[Route("/reports/StorageCustodyByPeriod")]
public class StorageCustodyByPeriodController(
    StorageCustodyByPeriodReportService service,
    IConfiguration configuration) : ControllerBase
{
    /// <summary>Mesma mensagem de StorageCustodyGuard.StandaloneOnlyMessage.</summary>
    public const string StandaloneOnlyMessage = "Disponível somente no modo STANDALONE.";

    [HttpPost]
    public async Task<IActionResult> Report([FromBody] StorageCustodyByPeriodRequest request)
    {
        if (!ErpMode.IsStandalone(configuration))
            return BadRequest(StandaloneOnlyMessage);

        if (LogisticsReportText.Validate(request) is { } error)
            return BadRequest(error);

        var pdf = await service.ExecuteAsync(request);

        Response.Headers.ContentDisposition = "inline; filename=\"storage-custody-by-period.pdf\"";
        return File(pdf, "application/pdf");
    }
}
