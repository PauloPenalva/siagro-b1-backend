using Microsoft.AspNetCore.Mvc;
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
}
