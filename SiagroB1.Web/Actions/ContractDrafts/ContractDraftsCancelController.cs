using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ContractDrafts;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.ContractDrafts;

public class ContractDraftsCancelController(ContractDraftsCancelService service) : ODataController
{
    [HttpPost("odata/ContractDraftsCancel")]
    public async Task<IActionResult> Cancel([FromBody] ODataActionParameters parameters, CancellationToken ct)
    {
        if (!ContractDraftActionParameters.TryGetGuid(parameters, "Key", out var key))
            return BadRequest("Minuta é obrigatória.");

        try
        {
            await service.ExecuteAsync(key, User.Identity?.Name ?? "Unknown", ct);
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
