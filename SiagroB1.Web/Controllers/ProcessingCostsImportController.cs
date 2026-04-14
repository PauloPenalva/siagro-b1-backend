using SiagroB1.Domain.Interfaces;

namespace SiagroB1.Web.Controllers;

using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/processing-costs")]
public class ProcessingCostsImportController : ControllerBase
{
    private readonly IProcessingCostCsvImportService _service;

    public ProcessingCostsImportController(IProcessingCostCsvImportService service)
    {
        _service = service;
    }

    [HttpPost("{code}/drying-parameters/import")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> ImportDryingParameters(
        [FromRoute] string code,
        [FromForm] IFormFile file,
        CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0)
            return BadRequest("Arquivo não informado.");

        var result = await _service.ImportDryingParametersAsync(code, file, cancellationToken);

        if (result.Errors.Count > 0)
            return BadRequest(result);

        return Ok(result);
    }

    [HttpPost("{code}/drying-details/import")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> ImportDryingDetails(
        [FromRoute] string code,
        [FromForm] IFormFile file,
        CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0)
            return BadRequest("Arquivo não informado.");

        var result = await _service.ImportDryingDetailsAsync(code, file, cancellationToken);

        if (result.Errors.Count > 0)
            return BadRequest(result);

        return Ok(result);
    }
}