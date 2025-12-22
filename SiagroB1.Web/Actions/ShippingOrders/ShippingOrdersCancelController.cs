using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.ShippingOrders;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.ShippingOrders;

public class ShippingOrdersCancelController(ShippingOrdersCancelService service) : ODataController
{
    [HttpPost]
    [Route("/odata/ShippingOrders({key:guid})/Cancel")]
    public async Task<IActionResult> Post([FromRoute] Guid key)
    {
        try
        {
            var username = User.Identity?.Name ?? "Unknown";
            
            await service.ExecuteAsync(key, username);
            return Ok();
        }
        catch (Exception e)
        {
            return e switch
            {
                NotFoundException => NotFound(e.Message),
                ApplicationException => BadRequest(e.Message),
                _ => StatusCode(500, e.Message)
            };
        }
    }
}