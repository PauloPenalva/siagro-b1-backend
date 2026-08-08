namespace SiagroB1.Web.Sockets.TruckScale;

using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

public static class TruckScaleWebSocketEndpoint
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static void MapTruckScaleWebSocket(this IEndpointRouteBuilder app)
    {
        // Canal de rede interna: fica na porta do Web e NÃO é exposto pelo Gateway. É isso que
        // dispensa autenticar o SiagroB1.Client.
        app.Map("/ws/truck-scale", HandleAsync);
    }

    private static async Task HandleAsync(HttpContext context)
    {
        var hub = context.RequestServices.GetRequiredService<TruckScaleHub>();
        var configProvider = context.RequestServices.GetRequiredService<ScaleConfigProvider>();
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("TruckScaleWebSocket");

        var scaleCode = context.Request.Query["truckScaleId"].ToString();

        if (string.IsNullOrEmpty(scaleCode) || !context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = 400;
            return;
        }

        var config = await configProvider.GetAsync(scaleCode);

        if (config == null)
        {
            logger.LogWarning("Balança {ScaleCode} não cadastrada; conexão recusada.", scaleCode);
            context.Response.StatusCode = 404;
            return;
        }

        var socket = await context.WebSockets.AcceptWebSocketAsync();
        hub.Add(scaleCode, socket);

        logger.LogInformation("Balança {ScaleCode} conectada.", scaleCode);

        try
        {
            await SendAsync(socket, new { action = "scale_config", data = config });

            var buffer = new byte[4096];

            while (socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(buffer, CancellationToken.None);

                if (result.MessageType == WebSocketMessageType.Close)
                    break;

                var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                var message = JsonSerializer.Deserialize<ScaleMessage>(json, JsonOptions);

                switch (message?.Action)
                {
                    case "weight_tick" when message.Data?.Weight is { } weight:
                        hub.PushWeight(scaleCode, weight);
                        break;

                    case "scale_status" when message.Data?.Online == false:
                        hub.SetOffline(scaleCode);
                        break;
                }
            }
        }
        catch (WebSocketException ex)
        {
            logger.LogWarning(ex, "Conexão da balança {ScaleCode} caiu.", scaleCode);
        }
        finally
        {
            hub.Remove(scaleCode);

            if (socket.State != WebSocketState.Closed && socket.State != WebSocketState.Aborted)
            {
                await socket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure, "closed", CancellationToken.None);
            }
        }
    }

    private static async Task SendAsync(WebSocket socket, object payload)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, JsonOptions));

        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
    }
}
