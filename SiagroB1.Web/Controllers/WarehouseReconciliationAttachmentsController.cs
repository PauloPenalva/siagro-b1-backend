using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.WarehouseReconciliations;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Controllers;

public class WarehouseReconciliationAttachmentsController(
    WarehouseReconciliationAttachmentsDeleteService service) : ODataController
{
    public async Task<IActionResult> Delete([FromRoute] Guid key)
    {
        try
        {
            await service.Delete(key);
            return NoContent();
        }
        catch (NotFoundException)
        {
            return NotFound();
        }
        catch (ApplicationException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}
