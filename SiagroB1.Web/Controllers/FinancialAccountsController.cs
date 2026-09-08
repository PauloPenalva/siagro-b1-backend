using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Controllers;

public class FinancialAccountsController(
    FinancialAccountsCreateService createService,
    FinancialAccountsUpdateService updateService,
    FinancialAccountsDeleteService deleteService,
    FinancialAccountsGetService getService) : ODataController
{
    [EnableQuery]
    public ActionResult<IEnumerable<FinancialAccount>> Get() => Ok(getService.QueryAll());

    [EnableQuery]
    public async Task<ActionResult<FinancialAccount>> Get([FromRoute] string key)
    {
        var entity = await getService.GetByIdAsync(key);
        return entity is null ? NotFound() : Ok(entity);
    }

    public async Task<IActionResult> Post([FromBody] FinancialAccount entity)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        try
        {
            var created = await createService.ExecuteAsync(entity, User.Identity?.Name ?? "Unknown");
            return Created(created);
        }
        catch (Exception e) { return Map(e); }
    }

    public async Task<IActionResult> Put([FromRoute] string key, [FromBody] FinancialAccount entity)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        try
        {
            await updateService.ExecuteAsync(key, entity);
            return NoContent();
        }
        catch (Exception e) { return Map(e); }
    }

    public async Task<IActionResult> Delete([FromRoute] string key)
    {
        try
        {
            await deleteService.ExecuteAsync(key);
            return NoContent();
        }
        catch (Exception e) { return Map(e); }
    }

    private IActionResult Map(Exception e) => e switch
    {
        NotFoundException or KeyNotFoundException => NotFound(e.Message),
        DefaultException or ApplicationException => BadRequest(e.Message),
        _ => StatusCode(500, e.Message)
    };
}
