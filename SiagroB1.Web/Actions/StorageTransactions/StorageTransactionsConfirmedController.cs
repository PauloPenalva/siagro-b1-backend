using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.StorageTransactions;

public class StorageTransactionsConfirmedController(
    // GAC-1232: a confirmação pela tela também dispara a liberação do transbordo em armazém
    // próprio — ver StorageTransactionsConfirmWithTransshipmentService.
    StorageTransactionsConfirmWithTransshipmentService service
    ) : ODataController
{

    [HttpPost("odata/StorageTransactionsConfirmed")]
    public async Task<IActionResult> Post(ODataActionParameters parameters)
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