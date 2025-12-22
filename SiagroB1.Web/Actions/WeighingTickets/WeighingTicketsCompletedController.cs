using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Dtos;
using SiagroB1.Application.WeighingTickets;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.WeighingTickets;

public class WeighingTicketsCompletedController(
    WeighingTicketsCompletedService service
    )
    :ODataController
{
    [HttpPost("odata/WeighingTicketsCompleted")]
    public async Task<ActionResult> Completed([FromRoute] Guid key, [FromBody] WeighingTicketsCompletedDto dto)
    {
        try
        {
            var userName = User.Identity?.Name ?? "Unknown";
            await service.ExecuteAsync(key, dto, userName);
            return Ok();
        }
        catch (Exception e)
        {
            if (e is KeyNotFoundException or NotFoundException)
            {
                return NotFound(e.Message);
            }
            
            return BadRequest(e.Message);
        }
        
    }
}