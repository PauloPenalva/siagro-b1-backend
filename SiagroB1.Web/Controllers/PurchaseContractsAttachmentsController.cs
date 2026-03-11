using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.PurchaseContracts;

namespace SiagroB1.Web.Controllers;

public class PurchaseContractsAttachmentsController(
    PurchaseContractsAttachmentsDeleteService service) : ODataController
{
    public async Task<IActionResult> Delete([FromRoute] Guid key)
    {
        await service.Delete(key);
        
        return NoContent();
    }
}