using System.Text.Json;

namespace SiagroB1.Web.Sockets;

public class WsMessageHandler
{
    private readonly PendingRequestStore _store;

    public async Task HandleAsync(string truckScaleId, string json)
    {
        var msg = JsonSerializer.Deserialize<WsMessage>(json);

        if (msg.Action == "weight_result")
        {
            _store.Resolve(msg.RequestId, json);
        }
    }
}