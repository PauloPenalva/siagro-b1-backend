using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Dtos;
using SiagroB1.Application.StorageAddresses;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.StorageAddresses;

public class StorageAddressesTotalsController(
    StorageAddressesTotalsService service
    ) : ODataController
{
    [HttpPost]
    [Route("odata/StorageAddresses({key:guid})/Totals")]
    [EnableQuery]
    public async Task<ActionResult<StorageAddressTotalsDto>> Totals([FromRoute] Guid key)
    {
        try
        {
            var totals = await service.ExecuteAsync(key);
            return Ok(totals);
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