using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.WeighingTickets;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.WeighingTickets;

public class WeighingTicketsSecondWeighingController(
    WeighingTicketsSecondWeighingService service
    )
    :ODataController
{
    [HttpPost("odata/WeighingTicketsSecondWeighing")]
    public async Task<ActionResult> SecondWeighing(ODataActionParameters parameters)
    {
        try
        {
            if (!parameters.TryGetValue("Key", out var keyObj) ||
                !parameters.TryGetValue("Value",  out var valueObj))
            {
                return BadRequest("Missing required parameters");
            }
            var key = Guid.Parse(keyObj.ToString());
            var value = int.Parse(valueObj.ToString());

            // Parâmetro string do OData é anulável: TryGetValue devolve true com valor null.
            var comments = parameters.TryGetValue("Comments", out var commentsObj)
                ? commentsObj?.ToString()
                : null;

            Guid? captureId = null;
            if (parameters.TryGetValue("CaptureId", out var captureObj)
                && Guid.TryParse(captureObj?.ToString(), out var parsed))
            {
                captureId = parsed;
            }

            var userName = User.Identity?.Name ?? "Unknown";

            await service.ExecuteAsync(key, value, comments, userName, captureId);
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