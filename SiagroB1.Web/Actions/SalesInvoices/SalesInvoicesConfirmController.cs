using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.SalesInvoices;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.SalesInvoices;

public class SalesInvoicesConfirmController(
    SalesInvoicesConfirmService service
    )
    :ODataController
{
    [HttpPost("odata/SalesInvoicesConfirm")]
    public async Task<IActionResult> PostAsync(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            if (!parameters.TryGetValue("Key", out var keyObj))
            {
                return BadRequest("Missing required parameters");
            }
            
            var userName = User.Identity?.Name ?? "Unknown";
            var key = Guid.Parse(keyObj.ToString());
            
            await service.ExecuteAsync(key,userName);
            
            return Ok();
        }
        catch (Exception e)
        {
            if (e is KeyNotFoundException or NotFoundException)
            {
                return NotFound(e.Message);
            }
            
            return BadRequest(e.Message);
        }
    }
}