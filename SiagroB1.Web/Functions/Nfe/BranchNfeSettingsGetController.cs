using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Functions.Nfe;

public class BranchNfeSettingsGetController(BranchNfeSettingsService service) : ODataController
{
    [HttpGet("odata/BranchNfeSettingsGet(BranchCode={branchCode})")]
    public async Task<IActionResult> GetAsync([FromRoute] string branchCode)
    {
        try
        {
            // Rotas por atributo entregam o segmento COM as aspas simples do OData.
            return Ok(await service.GetAsync(branchCode.Trim('\'')));
        }
        catch (DefaultException e)
        {
            return BadRequest(e.Message);
        }
    }
}
