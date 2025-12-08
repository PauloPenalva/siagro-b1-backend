using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.SalesContracts;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.SalesContracts;

public class SalesContractsCopyController(
    SalesContractsCopyService service
    ) : ODataController
{
    [HttpPost("odata/SalesContractsCopy")]
    public async Task<ActionResult> Post(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);
        
        try
        {   
            if (!parameters.TryGetValue("Key", out var keyObj)) {
                return BadRequest("Missing required parameters");
            }
            
            var key = Guid.Parse(keyObj.ToString());
            var userName = User.Identity?.Name ?? "Unknown";
            
            await service.ExecuteAsync(key, userName);
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