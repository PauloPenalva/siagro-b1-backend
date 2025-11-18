
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Dtos;
using SiagroB1.Application.PurchaseContracts;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Functions.PurchaseContracts;

public class PurchaseContractsGetTotalsController(
    PurchaseContractsTotalsService service
    ) : ODataController
{
    [HttpGet("odata/PurchaseContracts({key:guid})/GetTotals")]
    [EnableQuery]
    public async Task<ActionResult<PurchaseContractTotalsDto>> GetTotals([FromODataUri] Guid key)
    {
        try
        {
            var totals = await service.GetTotals(key);
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