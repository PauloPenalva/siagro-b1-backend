using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.StorageAddresses;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.StorageAddresses;

public class StorageAddressesReprocessingController(
    StorageAddressesReprocessingService service
    ) : ODataController
{

    [HttpPost("odata/StorageAddressesReprocessing")]
    public async Task<IActionResult> Post(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);
        
        try
        {
            if (!parameters.TryGetValue("FromDate", out var fromDate) ||
                !parameters.TryGetValue("ToDate", out var toDate) ||
                !parameters.TryGetValue("Code", out var code))
            {
                return BadRequest("Missing required parameters");
            }
            
            var initialDate = DateTime.Parse(fromDate.ToString());
            var finalDate = DateTime.Parse(toDate.ToString());
            var storageAddressCode = code.ToString();
            
            await service.ReprocessAsync(storageAddressCode, initialDate, finalDate);
            
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