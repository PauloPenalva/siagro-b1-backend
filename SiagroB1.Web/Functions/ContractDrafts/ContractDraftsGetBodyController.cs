using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ContractDrafts;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Functions.ContractDrafts;

public class ContractDraftsGetBodyController(ContractDraftsGetService service) : ODataController
{
    [HttpGet("odata/ContractDraftsGetBody(Key={key})")]
    public async Task<ActionResult<string>> GetBody([FromRoute] Guid key)
    {
        try { return Ok(await service.GetBodyAsync(key)); }
        catch (NotFoundException e) { return NotFound(e.Message); }
    }
}
