using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Dtos;
using SiagroB1.Application.SalesContracts;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Functions;

public class SalesContractsGetTotalsController(
    SalesContractsTotalsService service
    ) : ODataController
{
    [EnableQuery]
    [HttpGet("odata/SalesContractsGetTotals(key={key})")]
    public async Task<ActionResult<SalesContractTotalsResponseDto>> GetAsync([FromRoute] Guid key)
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