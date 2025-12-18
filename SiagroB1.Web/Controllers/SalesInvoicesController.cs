using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.SalesInvoices;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Shared.Base.Exceptions;

namespace SiagroB1.Web.Controllers;

public class SalesInvoicesController(
    SalesInvoicesCreateService createService
    )
    :ODataController
{
    public async Task<IActionResult> Post([FromBody] SalesInvoice entity)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }
            
        try
        {
            var userName = User.Identity?.Name ?? "Unknown";
            await createService.ExecuteAsync(entity, userName);

            return Created(entity);
        }
        catch (Exception ex)
        {
            if (ex is DefaultException)
            {
                return BadRequest(ex.Message);
            }

            return StatusCode(500, ex.Message);
        }
    }
}