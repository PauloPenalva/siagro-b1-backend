using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.SalesContracts;

public class SalesContractsReopenController(
    SalesContractsReopenService service) : ODataController
{
    [HttpPost("odata/SalesContractsReopen")]
    public async Task<IActionResult> ReopenAsync(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            if (!parameters.TryGetValue("Key", out var keyObj))
                return BadRequest("Missing required parameters");

            var userName = User.Identity?.Name ?? "Unknown";
            var key = Guid.Parse(keyObj.ToString());
            await service.ExecuteAsync(key, userName);
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
