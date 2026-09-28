using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ContractDrafts;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.ContractDrafts;

public class ContractDraftsDeleteController(ContractDraftsDeleteService service) : ODataController
{
    [HttpPost("odata/ContractDraftsDelete")]
    public async Task<IActionResult> Delete([FromBody] ODataActionParameters parameters)
    {
        if (!ContractDraftActionParameters.TryGetGuid(parameters, "Key", out var key))
            return BadRequest("Minuta é obrigatória.");

        try
        {
            await service.ExecuteAsync(key);
            return NoContent();
        }
        catch (Exception e)
        {
            if (e is NotFoundException or KeyNotFoundException) return NotFound(e.Message);
            if (e is DefaultException or BusinessException or ApplicationException) return BadRequest(e.Message);
            return StatusCode(500, e.Message);
        }
    }
}
