using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Web.Actions.ShipmentLoads;

public class ShipmentLoadsRecalculateInvoicedController(
    ShipmentLoadsRecalculateInvoicedService recalculateService,
    IUnitOfWork unitOfWork
    ) : ODataController
{
    [HttpPost("odata/ShipmentLoadsRecalculateInvoiced")]
    public async Task<ActionResult> Recalculate(ODataActionParameters parameters)
    {
        try
        {
            if (parameters == null)
            {
                return BadRequest("Missing required parameters");
            }

            if (!parameters.TryGetValue("Key", out var keyObj) || keyObj == null)
            {
                return BadRequest("Missing required parameters");
            }

            var key = Guid.Parse(keyObj.ToString()!);

            await unitOfWork.BeginTransactionAsync();
            await recalculateService.RecalculateAsync(key);
            await unitOfWork.SaveChangesAsync();
            await unitOfWork.CommitAsync();

            return Ok();
        }
        catch (Exception e)
        {
            await unitOfWork.RollbackAsync();

            if (e is KeyNotFoundException or NotFoundException)
            {
                return NotFound();
            }

            return BadRequest(e.Message);
        }
    }
}
