using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.RoyaltyWriteOffs;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.RoyaltyWriteOffs;

public class RoyaltyWriteOffsCancelController(RoyaltyWriteOffsCancelService service) : ODataController
{
    [HttpPost("odata/RoyaltyWriteOffsCancel")]
    public async Task<IActionResult> Post(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            if (parameters is null || !parameters.TryGetValue("Key", out var keyObj) || keyObj is null)
                return BadRequest("Missing required parameters");

            await service.ExecuteAsync(Guid.Parse(keyObj.ToString()!), User.Identity?.Name ?? "Unknown");
            return Ok();
        }
        catch (Exception e)
        {
            if (e is KeyNotFoundException or NotFoundException)
                return NotFound(e.Message);
            return BadRequest(e.Message);
        }
    }
}
