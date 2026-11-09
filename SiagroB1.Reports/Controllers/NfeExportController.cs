using Microsoft.AspNetCore.Mvc;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;

namespace SiagroB1.Reports.Controllers;

/// <summary>ZIP com XMLs e DANFEs das NF-e do período, para entregar ao contador. Só STANDALONE.</summary>
[ApiController]
[Route("/reports/NfeExport")]
public class NfeExportController(NfeExportService service, IConfiguration configuration) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Post([FromBody] NfeExportRequest request)
    {
        if (!ErpMode.IsStandalone(configuration))
            return BadRequest(FiscalConferenceController.StandaloneOnlyMessage);

        try
        {
            var (zip, fileName) = await service.BuildAsync(request);
            return File(zip, "application/zip", fileName);
        }
        catch (DefaultException e)
        {
            return BadRequest(e.Message);
        }
    }
}
