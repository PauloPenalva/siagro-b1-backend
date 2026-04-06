using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services;

namespace SiagroB1.Web.Functions.SystemSetup;

public class SystemSetupGetActiveController(
    SystemSetupService service
    ) : ODataController
{
    [HttpGet("odata/SystemSetupGetActive()")]
    public async Task<ActionResult> GetActive()
    {
        var setup = await service.QueryAll()
            .Where(x => x.IsActive)
            .FirstOrDefaultAsync();
        
        return Ok(setup);
    }
}