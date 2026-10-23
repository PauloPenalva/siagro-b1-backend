using Microsoft.AspNetCore.Mvc;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services.Ecf;

namespace SiagroB1.Reports.Controllers;

/// <summary>ECF (Escrituração Contábil Fiscal, Lucro Real): prévia das inconsistências e geração do arquivo TXT. Só STANDALONE.</summary>
[ApiController]
[Route("/reports/Ecf")]
public class EcfController(EcfFileService service, IConfiguration configuration) : ControllerBase
{
    [HttpPost("Check")]
    public async Task<IActionResult> Check([FromBody] EcfRequest request)
    {
        var refusal = Refusal(request);
        if (refusal != null) return refusal;

        try
        {
            return Ok(await service.CheckAsync(request));
        }
        catch (BusinessException e)
        {
            return BadRequest(e.Message);
        }
    }

    [HttpPost]
    public async Task<IActionResult> Generate([FromBody] EcfRequest request)
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

    private BadRequestObjectResult? Refusal(EcfRequest request)
    {
        if (!ErpMode.IsStandalone(configuration))
            return BadRequest(FiscalConferenceController.StandaloneOnlyMessage);
        if (string.IsNullOrWhiteSpace(request.BranchCode) || request.Year is < 2000 or > 9999)
            return BadRequest("Informe a filial e um ano válido.");
        if (request.Purpose is not (0 or 1))
            return BadRequest("Finalidade inválida: 0 original ou 1 retificadora.");
        return null;
    }
}
