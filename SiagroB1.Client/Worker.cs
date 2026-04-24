using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using SiagroB1.Client.Dtos;
using SiagroB1.Client.Interfaces;

namespace SiagroB1.Client;

public class Worker(IConfiguration _config, IScaleReader _scale, ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _ = _scale.StartAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var ws = new ClientWebSocket();

                var url = $"{_config["WebSocketUrl"]}?truckScaleId={_config["TruckScaleId"]}";
                await ws.ConnectAsync(new Uri(url), stoppingToken);

                var buffer = new byte[4096];

                while (ws.State == WebSocketState.Open)
                {
                    var result = await ws.ReceiveAsync(buffer, stoppingToken);
                    var json = Encoding.UTF8.GetString(buffer, 0, result.Count);

                    var msg = JsonSerializer.Deserialize<WsMessage>(json,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (msg?.Action == "capture_weight")
                    {
                        var weight = _scale.GetWeight();

                        var response = new
                        {
                            action = "weight_result",
                            requestId = msg.RequestId,
                            data = new { weight, stable = true }
                        };

                        var respJson = JsonSerializer.Serialize(response);
                        var bytes = Encoding.UTF8.GetBytes(respJson);

                        await ws.SendAsync(bytes, WebSocketMessageType.Text, true, stoppingToken);
                    }
                }
            }
            catch
            {
                await Task.Delay(5000, stoppingToken);
            }
        }
    }
}