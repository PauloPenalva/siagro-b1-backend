using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace SiagroB1.Web.Sockets.TruckScale;

[ApiController]
[Route("api/TruckScale")]
public class TruckScaleCaptureController(
    TruckScaleWebSocketConnectionManager manager,
    PendingRequestStore store
    ) : Controller
{
    [HttpPost("{code}/capture")]
    public async Task<IActionResult> Post([FromRoute] string code)
    {var ws = manager.Get(code);

        if (ws == null)
            return BadRequest("Balança offline");

        var requestId = Guid.NewGuid().ToString();

        var command = new
        {
            action = "capture_weight",
            requestId
        };

        var json = JsonSerializer.Serialize(command);
        var bytes = Encoding.UTF8.GetBytes(json);
        
        var task = store.Create(requestId);
        
        await ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);

        var completed = await Task.WhenAny(task, Task.Delay(15000));

        if (completed != task)
            return StatusCode(StatusCodes.Status504GatewayTimeout);

        var response = await task;

        return Ok(response);
        
    }
}