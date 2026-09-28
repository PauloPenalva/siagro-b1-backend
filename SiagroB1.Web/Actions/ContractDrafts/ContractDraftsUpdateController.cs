using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ContractDrafts;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.ContractDrafts;

public class ContractDraftsUpdateController(ContractDraftsUpdateService service) : ODataController
{
    [HttpPost("odata/ContractDraftsUpdate")]
    public async Task<IActionResult> Update([FromBody] ODataActionParameters parameters)
    {
        if (!ContractDraftActionParameters.TryGetGuid(parameters, "Key", out var key))
            return BadRequest("Minuta é obrigatória.");
        if (!ContractDraftActionParameters.TryGetDraftType(parameters, out var draftType))
            return BadRequest(ContractDraftActionParameters.InvalidDraftTypeMessage);

        var description = ContractDraftActionParameters.GetString(parameters, "Description") ?? "";
        var bodyHtml = ContractDraftActionParameters.GetString(parameters, "BodyHtml") ?? "";
        var userName = User.Identity?.Name ?? "Unknown";

        try
        {
            await service.ExecuteAsync(key, description, draftType, bodyHtml, userName);
            return Ok();
        }
        catch (Exception e)
        {
            if (e is NotFoundException or KeyNotFoundException) return NotFound(e.Message);
            if (e is DefaultException or BusinessException or ApplicationException) return BadRequest(e.Message);
            return StatusCode(500, e.Message);
        }
    }
}
