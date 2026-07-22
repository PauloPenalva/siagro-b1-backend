using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.SalesContracts;

namespace SiagroB1.Web.Actions.SalesContracts;

public class SalesContractsRecalculateAllBalancesController(
    SalesContractsRecalculateBalanceService service) : ODataController
{
    [HttpPost("odata/SalesContractsRecalculateAllBalances")]
    public async Task<IActionResult> RecalculateAllAsync(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            var result = await service.ExecuteAllAsync();
            return Ok(result);
        }
        catch (Exception e)
        {
            return BadRequest(e.Message);
        }
    }
}
