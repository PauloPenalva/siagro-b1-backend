using Microsoft.AspNetCore.Mvc;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Reports.Services;

namespace SiagroB1.Reports.Controllers;

/// <summary>DAMDFE do MDF-e autorizado (ou encerrado/cancelado, com tarja). Só STANDALONE.</summary>
[ApiController]
[Route("/reports/Damdfe")]
public class DamdfeController(DamdfeReportService service, IConfiguration configuration) : ControllerBase
{
    public const string StandaloneOnlyMessage = "O MDF-e só existe no modo STANDALONE.";

    [HttpGet("{key:guid}")]
    public async Task<IActionResult> Get(Guid key)
    {
        if (!ErpMode.IsStandalone(configuration))
            return BadRequest(StandaloneOnlyMessage);

        try
        {
            var dto = await service.LoadAsync(key);
            using var report = service.Prepare(dto);
            var pdf = PdfExporter.Export(report, $"DAMDFE {dto.AccessKey}");
            Response.Headers.ContentDisposition = $"inline; filename=\"{dto.AccessKey}-damdfe.pdf\"";
            return File(pdf, "application/pdf");
        }
        catch (DefaultException e)
        {
            return BadRequest(e.Message);
        }
    }
}
