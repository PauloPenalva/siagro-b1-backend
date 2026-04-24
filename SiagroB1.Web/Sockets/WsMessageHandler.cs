using System.Text.Json;

namespace SiagroB1.Web.Sockets;

public class WsMessageHandler(PendingRequestStore store)
{
    public async Task HandleAsync(string truckScaleId, string json)
    {
        
        Console.WriteLine(">>> HANDLE FOI CHAMADO");
        Console.WriteLine(json);
        
        var msg = JsonSerializer.Deserialize<WsMessage>(
            json,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

        Console.WriteLine($"Action: {msg?.Action}");
        Console.WriteLine($"RequestId: {msg?.RequestId}");

        if (msg.Action == "weight_result")
        {
            Console.WriteLine($"RESOLVENDO: {msg.RequestId}");
            store.Resolve(msg.RequestId, json);
        }
    }
}