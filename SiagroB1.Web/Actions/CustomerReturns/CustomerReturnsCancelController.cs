using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.CustomerReturns;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.CustomerReturns;

public class CustomerReturnsCancelController(CustomerReturnsCancelService service)
    : ODataController
{
    [HttpPost("odata/CustomerReturnsCancel")]
    public async Task<IActionResult> PostAsync(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            if (!parameters.TryGetValue("Key", out var keyObj) || keyObj is null)
                return BadRequest("Missing required parameters");

            var userName = User.Identity?.Name ?? "Unknown";

            await service.ExecuteAsync(Guid.Parse(keyObj.ToString()!), userName);

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
