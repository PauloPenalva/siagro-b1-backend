using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Web.Controllers;

/// <summary>Lançamentos manuais da conta. Inclusão é pela ação FinancialAccountEntriesCreate (valor em Double).</summary>
public class FinancialAccountEntriesController(IUnitOfWork db, FinancialAccountEntriesDeleteService deleteService)
    : ODataController
{
    [EnableQuery]
    public ActionResult<IEnumerable<FinancialAccountEntry>> Get() => Ok(db.Context.FinancialAccountEntries);

    public async Task<IActionResult> Delete([FromRoute] Guid key)
    {
        try
        {
            await deleteService.ExecuteAsync(key, User.Identity?.Name ?? "Unknown");
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
