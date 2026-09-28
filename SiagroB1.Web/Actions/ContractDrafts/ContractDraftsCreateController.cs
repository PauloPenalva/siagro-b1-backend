using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ContractDrafts;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.ContractDrafts;

public class ContractDraftsCreateController(ContractDraftsCreateService service) : ODataController
{
    [HttpPost("odata/ContractDraftsCreate")]
    public async Task<IActionResult> Create([FromBody] ODataActionParameters parameters, CancellationToken ct)
    {
        // parameters chega NULO quando nenhum parâmetro do EDM é enviado.
        if (!ContractDraftActionParameters.TryGetContractType(parameters, out var contractType))
            return BadRequest(ContractDraftActionParameters.InvalidContractTypeMessage);
        if (!ContractDraftActionParameters.TryGetGuid(parameters, "ContractKey", out var contractKey))
            return BadRequest("Contrato é obrigatório.");
        if (!ContractDraftActionParameters.TryGetGuid(parameters, "TemplateKey", out var templateKey))
            return BadRequest("Modelo é obrigatório.");
        if (!ContractDraftActionParameters.TryGetDraftType(parameters, out var draftType))
            return BadRequest(ContractDraftActionParameters.InvalidDraftTypeMessage);

        var description = ContractDraftActionParameters.GetString(parameters, "Description") ?? "";
        var userName = User.Identity?.Name ?? "Unknown";

        try
        {
            var draft = await service.ExecuteAsync(contractType, contractKey, templateKey, draftType, description, userName, ct);
            return Ok(new { draft.Key, draft.Sequence });
        }
        catch (Exception e)
        {
            if (e is NotFoundException or KeyNotFoundException) return NotFound(e.Message);
            if (e is DefaultException or BusinessException or ApplicationException) return BadRequest(e.Message);
            return StatusCode(500, e.Message);
        }
    }
}
