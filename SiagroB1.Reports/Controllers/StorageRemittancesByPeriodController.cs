using Microsoft.AspNetCore.Mvc;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Helpers;
using SiagroB1.Reports.Services;

namespace SiagroB1.Reports.Controllers;

[ApiController]
[Route("/reports/StorageRemittancesByPeriod")]
public class StorageRemittancesByPeriodController(
    StorageRemittancesByPeriodReportService service,
    IConfiguration configuration) : ControllerBase
{
    /// <summary>Mesma mensagem de <c>StorageDepositRules.StandaloneOnlyMessage</c>: a remessa só existe em STANDALONE.</summary>
    public const string StandaloneOnlyMessage = "Disponível somente no modo STANDALONE.";

    [HttpPost]
    public async Task<IActionResult> Report([FromBody] StorageRemittancesByPeriodRequest request)
    {
        if (!ErpMode.IsStandalone(configuration))
            return BadRequest(StandaloneOnlyMessage);

        if (LogisticsReportText.Validate(request) is { } error)
            return BadRequest(error);

        var pdf = await service.ExecuteAsync(request);

        Response.Headers.ContentDisposition = "inline; filename=\"storage-remittances-by-period.pdf\"";
        return File(pdf, "application/pdf");
    }
}
