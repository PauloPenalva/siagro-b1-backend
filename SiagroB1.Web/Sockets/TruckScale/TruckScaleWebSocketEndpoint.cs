namespace SiagroB1.Web.Sockets.TruckScale;

using System.Net.WebSockets;
using System.Text;

public static class TruckScaleWebSocketEndpoint
{
    public static void MapTruckScaleWebSocket(this IEndpointRouteBuilder app)
    {
        app.Map("/ws/truck-scale", HandleAsync);
    }

    private static async Task HandleAsync(HttpContext context)
    {
        var manager = context.RequestServices.GetRequiredService<TruckScaleWebSocketConnectionManager>();
        var handler = context.RequestServices.GetRequiredService<WsMessageHandler>();

        var truckScaleId = context.Request.Query["truckScaleId"];

        if (string.IsNullOrEmpty(truckScaleId))
        {
            context.Response.StatusCode = 400;
            return;
        }

        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = 400;
            return;
        }

        var socket = await context.WebSockets.AcceptWebSocketAsync();
        manager.Add(truckScaleId, socket);

        var buffer = new byte[4096];

        try
        {
            while (socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(buffer, CancellationToken.None);

                if (result.MessageType == WebSocketMessageType.Close)
                    break;

                var json = Encoding.UTF8.GetString(buffer, 0, result.Count);

                await handler.HandleAsync(truckScaleId, json);
            }
        }
        finally
        {
            manager.Remove(truckScaleId);

            if (socket.State != WebSocketState.Closed)
            {
                await socket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "closed",
                    CancellationToken.None
                );
            }
        }
    }
}