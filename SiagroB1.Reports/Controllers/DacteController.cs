using Microsoft.AspNetCore.Mvc;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Reports.Services;

namespace SiagroB1.Reports.Controllers;

/// <summary>DACTE do CT-e autorizado (ou cancelado, com tarja). Só STANDALONE.</summary>
[ApiController]
[Route("/reports/Dacte")]
public class DacteController(DacteReportService service, IConfiguration configuration) : ControllerBase
{
    public const string StandaloneOnlyMessage = "O CT-e só existe no modo STANDALONE.";

    [HttpGet("{key:guid}")]
    public async Task<IActionResult> Get(Guid key)
    {
        if (!ErpMode.IsStandalone(configuration))
            return BadRequest(StandaloneOnlyMessage);

        try
        {
            var dto = await service.LoadAsync(key);
            using var report = service.Prepare(dto);
            var pdf = PdfExporter.Export(report, $"DACTE {dto.AccessKey}");
            Response.Headers.ContentDisposition = $"inline; filename=\"{dto.AccessKey}-dacte.pdf\"";
            return File(pdf, "application/pdf");
        }
        catch (DefaultException e)
        {
            return BadRequest(e.Message);
        }
    }
}
