using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.WarehouseReconciliations;

namespace SiagroB1.Web.Functions.WarehouseReconciliations;

public class WarehouseReconciliationsListReleasesController(
    WarehouseReconciliationsListReleasesService service) : ODataController
{
    [HttpGet("odata/WarehouseReconciliationsListReleases(Key={key})")]
    public async Task<ActionResult> List([FromRoute] Guid key) => Ok(await service.ExecuteAsync(key));
}
