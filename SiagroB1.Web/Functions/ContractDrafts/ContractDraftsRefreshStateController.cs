using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ContractDrafts;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Functions.ContractDrafts;

public class ContractDraftsRefreshStateController(ContractDraftsRefreshStateService service) : ODataController
{
    [HttpGet("odata/ContractDraftsRefreshState(Key={key})")]
    public async Task<ActionResult<bool>> Refresh([FromRoute] Guid key, CancellationToken ct)
    {
        try { return Ok(await service.ExecuteAsync(key, User.Identity?.Name ?? "Unknown", ct)); }
        catch (NotFoundException e) { return NotFound(e.Message); }
        catch (BusinessException e) { return BadRequest(e.Message); }
    }
}
