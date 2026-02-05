using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.SalesContracts;

namespace SiagroB1.Web.Controllers;

public class SalesContractsAttachmentsController(
   SalesContractsAttachmentsDeleteService service) : ODataController
{
    public async Task<IActionResult> Delete([FromRoute] Guid key)
    {
        await service.Delete(key);
        
        return NoContent();
    }
}