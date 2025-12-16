using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.ShipmentReleases;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.ShipmentReleases;

public class ShipmentReleasesCancelationController(
    ShipmentReleasesCancelationService cancelationService
    ) : ODataController
{
    [HttpPost("odata/ShipmentReleasesCancelation")]
    public async Task<ActionResult> Cancelation(ODataActionParameters parameters)
    {
        try
        {
            if (!parameters.TryGetValue("RowId", out var rowIdObj))
            {
                return BadRequest("Missing required parameters");
            }
            var rowId = int.Parse(rowIdObj.ToString());
            
            await cancelationService.ExecuteAsync(rowId);
            return Ok();
        }
        catch (Exception e)
        {
            if (e is KeyNotFoundException or NotFoundException)
            {
                return NotFound();
            }
            
            return BadRequest(e.Message);
        }
       
    }
}