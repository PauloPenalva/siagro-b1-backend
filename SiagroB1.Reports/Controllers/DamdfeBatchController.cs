using Microsoft.AspNetCore.Mvc;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Reports.Services;

namespace SiagroB1.Reports.Controllers;

/// <summary>ZIP de DAMDFEs dos MDF-e autorizados selecionados (máx. 50). Só STANDALONE.</summary>
[ApiController]
[Route("/reports/DamdfeBatch")]
public class DamdfeBatchController(DamdfeBatchService service, IConfiguration configuration) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Post([FromBody] DamdfeBatchRequest request)
    {
        if (!ErpMode.IsStandalone(configuration))
            return BadRequest(DamdfeController.StandaloneOnlyMessage);

        try
        {
            var zip = await service.BuildAsync(request?.Keys ?? []);
            return File(zip, "application/zip", "damdfe-mdfe.zip");
        }
        catch (DefaultException e)
        {
            return BadRequest(e.Message);
        }
    }

    public sealed class DamdfeBatchRequest
    {
        public List<Guid> Keys { get; set; } = [];
    }
}
