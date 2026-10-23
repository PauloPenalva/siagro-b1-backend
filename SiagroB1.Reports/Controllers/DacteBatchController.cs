using Microsoft.AspNetCore.Mvc;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Reports.Services;

namespace SiagroB1.Reports.Controllers;

/// <summary>ZIP de DACTEs dos CT-e autorizados selecionados (máx. 200). Só STANDALONE.</summary>
[ApiController]
[Route("/reports/DacteBatch")]
public class DacteBatchController(DacteBatchService service, IConfiguration configuration) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Post([FromBody] DacteBatchRequest request)
    {
        if (!ErpMode.IsStandalone(configuration))
            return BadRequest(DacteController.StandaloneOnlyMessage);

        try
        {
            var zip = await service.BuildAsync(request?.Keys ?? []);
            return File(zip, "application/zip", "dacte-cte.zip");
        }
        catch (DefaultException e)
        {
            return BadRequest(e.Message);
        }
    }

    public sealed class DacteBatchRequest
    {
        public List<Guid> Keys { get; set; } = [];
    }
}
