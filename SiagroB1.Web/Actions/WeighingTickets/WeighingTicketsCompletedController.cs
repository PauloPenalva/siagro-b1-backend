using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.WeighingTickets;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.WeighingTickets;

public class WeighingTicketsCompletedController(
    WeighingTicketsCompletedService service
    )
    :ODataController
{
    [HttpPost("odata/WeighingTicketsCompleted")]
    public async Task<ActionResult> Completed(ODataActionParameters parameters)
    {
        try
        {
            if (!parameters.TryGetValue("Key", out var keyObj) )
            {
                return BadRequest("Missing parameters");
            }
            
            var key = (Guid) keyObj;
            
            var userName = User.Identity?.Name ?? "Unknown";
            await service.ExecuteAsync(key, userName);
            return Ok();
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            
            if (e is KeyNotFoundException or NotFoundException)
            {
                return NotFound(e.Message);
            }
            
            return BadRequest(e.Message);
        }
        
    }
}