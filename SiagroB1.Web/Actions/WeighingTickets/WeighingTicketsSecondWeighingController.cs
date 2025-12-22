using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.WeighingTickets;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.WeighingTickets;

public class WeighingTicketsSecondWeighingController(
    WeighingTicketsSecondWeighingService service
    )
    :ODataController
{
    [HttpPost("odata/WeighingTicketsSecondWeighing")]
    public async Task<ActionResult> SecondWeighing(ODataActionParameters parameters)
    {
        try
        {
            if (!parameters.TryGetValue("Key", out var keyObj) ||
                !parameters.TryGetValue("Value",  out var valueObj))
            {
                return BadRequest("Missing required parameters");
            }
            var key = Guid.Parse(keyObj.ToString());
            var value = int.Parse(valueObj.ToString());
            
            await service.ExecuteAsync(key, value);
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