using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ShippingTransactions;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.ShippingTransactions;

public class ShippingTransactionsReverseController(
    ShippingTransactionsReverseService service
    )
    : ODataController
{

    [HttpPost("odata/ShippingTransactionsReverse")]
    public async Task<ActionResult> PostAsync(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);
        
        try
        {
            // parameters vem NULO quando o corpo não casa com nenhum parâmetro do EDM — sem a
            // guarda o TryGetValue estoura NRE e o cliente recebe um 500 de corpo vazio.
            if (parameters == null || !parameters.TryGetValue("Key", out var keyObj))
            {
                return BadRequest("Missing required parameters");
            }
            
            var userName = User.Identity?.Name ?? "Unknown";
            var key = (Guid) keyObj;
            
            await service.ExecuteAsync(key, userName);
            
            return Ok();
        }
        catch (Exception e)
        {
            if (e is NotFoundException or KeyNotFoundException)
            {
                return NotFound(e.Message);
            }

            if (e is ApplicationException)
            {
                return BadRequest(e.Message);
            }
            
            return StatusCode(500, e.Message);
        }
    }
}