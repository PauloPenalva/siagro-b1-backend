using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Web.Controllers;

/// <summary>Transferências entre contas. Inclusão é pela ação FinancialAccountTransfersCreate (valor em Double).</summary>
public class FinancialAccountTransfersController(IUnitOfWork db, FinancialAccountTransfersDeleteService deleteService)
    : ODataController
{
    [EnableQuery]
    public ActionResult<IEnumerable<FinancialAccountTransfer>> Get() => Ok(db.Context.FinancialAccountTransfers);

    public async Task<IActionResult> Delete([FromRoute] Guid key)
    {
        try
        {
            await deleteService.ExecuteAsync(key);
            return NoContent();
        }
        catch (Exception e)
        {
            return e switch
            {
                NotFoundException or KeyNotFoundException => NotFound(e.Message),
                DefaultException or ApplicationException => BadRequest(e.Message),
                _ => StatusCode(500, e.Message)
            };
        }
    }
}
