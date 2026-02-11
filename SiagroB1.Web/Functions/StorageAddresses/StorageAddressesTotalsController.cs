using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.StorageAddresses;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Functions.StorageAddresses;

public class StorageAddressesTotalsController(
    StorageAddressesTotalsService service
    ) : ODataController
{
    [HttpGet("odata/StorageAddressesTotals(Code={code})")]
    public async Task<ActionResult<StorageAddressTotalsDto>> Totals([FromRoute] string code)
    {
        try
        {
            var totals = await service.ExecuteAsync(code);
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