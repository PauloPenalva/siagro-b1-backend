using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.StorageAddresses;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Functions.StorageAddresses;

public class StorageAddressesListOpenedByItemController(
    StorageAddressesListOpenedByItemService service
    ) : ODataController
{
    [HttpGet("odata/StorageAddressesListOpenedByItem(Code={code})")]
    public async Task<ActionResult<StorageAddressTotalsDto>> List([FromRoute] string code)
    {
        try
        {
            var list = await service.ExecuteAsync(code);
            return Ok(list);
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