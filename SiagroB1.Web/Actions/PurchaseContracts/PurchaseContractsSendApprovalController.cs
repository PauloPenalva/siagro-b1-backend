using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Dtos;
using SiagroB1.Application.PurchaseContracts;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.PurchaseContracts;

public class PurchaseContractsSendApprovalController(
    PurchaseContractsSendApprovalService service
    ) : ODataController
{
    [HttpPost("odata/PurchaseContractsSendApproval")]
    public async Task<ActionResult> Post([FromBody] PurchaseContractActionRequestDto request)
    {
        try
        {
            var userName = User.Identity?.Name ?? "Unknown";
            
            await service.ExecuteAsync(request.Key, userName);
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