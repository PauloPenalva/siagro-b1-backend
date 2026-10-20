using Microsoft.AspNetCore.Mvc;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;

namespace SiagroB1.Reports.Controllers;

[ApiController]
[Route("/reports/FiscalConference")]
public class FiscalConferenceController(FiscalConferenceService service, IConfiguration configuration) : ControllerBase
{
    /// <summary>Mesma mensagem dos demais relatórios fiscais: a conferência só existe em STANDALONE.</summary>
    public const string StandaloneOnlyMessage = "Disponível somente no modo STANDALONE.";

    [HttpPost]
    public async Task<IActionResult> Get([FromBody] FiscalConferenceRequest request)
    {
        if (!ErpMode.IsStandalone(configuration))
            return BadRequest(StandaloneOnlyMessage);

        if (request.Month is < 1 or > 12 || request.Year is < 2000 or > 9999)
            return BadRequest("Informe um mês válido.");

        return Ok(await service.BuildAsync(request));
    }
}
