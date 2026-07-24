using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.SalesContracts;

public class SalesContractsCommentCreateController(
    SalesContractsCommentCreateService service) : ODataController
{
    [HttpPost("odata/SalesContractsCommentCreate")]
    public async Task<IActionResult> Post(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            if (!parameters.TryGetValue("ContractKey", out var keyObj))
                return BadRequest("Missing required parameters");

            parameters.TryGetValue("Text", out var textObj);

            await service.ExecuteAsync(
                (Guid) keyObj, textObj as string, User.Identity?.Name ?? "Unknown");

            return Ok();
        }
        catch (Exception e)
        {
            if (e is NotFoundException or KeyNotFoundException)
                return NotFound(e.Message);

            if (e is DefaultException or BusinessException or ApplicationException)
                return BadRequest(e.Message);

            return StatusCode(500, e.Message);
        }
    }
}
