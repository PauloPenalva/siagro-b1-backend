using Microsoft.AspNetCore.Mvc;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services.SpedFiscal;

namespace SiagroB1.Reports.Controllers;

/// <summary>SPED Fiscal (EFD ICMS/IPI): prévia das inconsistências e geração do arquivo TXT. Só STANDALONE.</summary>
[ApiController]
[Route("/reports/SpedFiscal")]
public class SpedFiscalController(SpedFiscalFileService service, IConfiguration configuration) : ControllerBase
{
    [HttpPost("Check")]
    public async Task<IActionResult> Check([FromBody] SpedFiscalRequest request)
    {
        var refusal = Refusal(request);
        if (refusal != null) return refusal;

        return Ok(await service.CheckAsync(request));
    }

    [HttpPost]
    public async Task<IActionResult> Generate([FromBody] SpedFiscalRequest request)
    {
        var refusal = Refusal(request);
        if (refusal != null) return refusal;

        try
        {
            var (content, fileName) = await service.GenerateAsync(request);
            return File(content, "text/plain; charset=iso-8859-1", fileName);
        }
        catch (BusinessException e)
        {
            return BadRequest(e.Message);
        }
    }

    private BadRequestObjectResult? Refusal(SpedFiscalRequest request)
    {
        if (!ErpMode.IsStandalone(configuration))
            return BadRequest(FiscalConferenceController.StandaloneOnlyMessage);
        if (string.IsNullOrWhiteSpace(request.BranchCode) || request.Month is < 1 or > 12 || request.Year is < 2000 or > 9999)
            return BadRequest("Informe a filial e um mês válido.");
        return null;
    }
}
