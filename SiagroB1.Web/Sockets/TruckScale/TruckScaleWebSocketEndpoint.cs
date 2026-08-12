namespace SiagroB1.Web.Sockets.TruckScale;

using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using SiagroB1.Commons.Scales;

public static class TruckScaleWebSocketEndpoint
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static void MapTruckScaleWebSocket(this IEndpointRouteBuilder app)
    {
        // O Gateway publica este caminho (rota `truck-scale-ws-route`) para alcançar o Client
        // instalado no PC da balança. Deixou de ser canal exclusivo de rede interna, e por isso a
        // conexão passou a exigir a chave compartilhada de ScaleClientAuth.
        app.Map("/ws/truck-scale", HandleAsync);
    }

    private static async Task HandleAsync(HttpContext context)
    {
        var hub = context.RequestServices.GetRequiredService<TruckScaleHub>();
        var configProvider = context.RequestServices.GetRequiredService<ScaleConfigProvider>();
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("TruckScaleWebSocket");

        var scaleCode = context.Request.Query["truckScaleId"].ToString();

        if (string.IsNullOrEmpty(scaleCode))
        {
            context.Response.StatusCode = 400;
            return;
        }

        var configuration = context.RequestServices.GetRequiredService<IConfiguration>();

        // Antes do IsWebSocketRequest e antes de consultar o cadastro, de propósito: a diferença
        // entre 404 e 101 revela quais códigos de balança existem, e quem não tem a chave não deve
        // conseguir sondar isso.
        if (!ScaleClientAuth.IsAuthorized(
                configuredKey: configuration[ScaleClientAuth.ConfigurationKey],
                presentedKey: context.Request.Headers[ScaleClientAuth.HeaderName]))
        {
            logger.LogWarning(
                "Chave inválida na conexão da balança {ScaleCode} vinda de {RemoteIp}.",
                scaleCode,
                context.Connection.RemoteIpAddress);

            context.Response.StatusCode = 401;
            return;
        }

        if (!context.WebSockets.IsWebSocketRequest)
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
