using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.PurchaseContracts;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.PurchaseContracts;

public class PurchaseContractsTotalsController(
    PurchaseContractsTotalsService service
    ) : ODataController
{
    [HttpPost]
    [Route("odata/PurchaseContractsTotals")]
    [EnableQuery]
    public async Task<ActionResult<PurchaseContractTotalsResponseDto>> Totals([FromBody] PurchaseContractActionRequestDto request)
    {
        try
        {
            var totals = await service.GetTotals(request.Key);
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