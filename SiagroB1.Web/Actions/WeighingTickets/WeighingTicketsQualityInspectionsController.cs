using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.WeighingTickets;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.WeighingTickets;

public class WeighingTicketsQualityInspectionsController(
    WeighingTicketsQualityInspectionsService service
    )
    :ODataController
{
    [HttpPost("odata/WeighingTicketsQualityInspections")]
    public async Task<ActionResult> QualityInspections([FromRoute] Guid key, 
        [FromBody] List<WeighingTicketsQualityInspectionsDto> inspections)
    {
        try
        {
            var userName = User.Identity?.Name ?? "Unknown";
            await service.ExecuteAsync(key, inspections);
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