using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Dtos;
using SiagroB1.Application.WeighingTickets;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.WeighingTickets;

public class WeighingTicketsSecondWeighingController(
    WeighingTicketsSecondWeighingService service
    )
    :ODataController
{
    [HttpPost]
    [Route("odata/WeighingTickets({key:guid})/SecondWeighing")]
    [EnableQuery]
    public async Task<ActionResult> SecondWeighing([FromRoute] Guid key,  [FromBody] WeighingTicketsWeighDto dto)
    {
        try
        {
            await service.ExecuteAsync(key, dto);
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