using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using SiagroB1.Client.Dtos;
using SiagroB1.Client.Readers;

namespace SiagroB1.Client;

/// <summary>
/// Uma balança, uma conexão. O Client é produtor: recebe a configuração do servidor e transmite o
/// peso continuamente, sem esperar por pedido.
/// </summary>
public class ScaleWorker(
    string scaleCode,
    IConfiguration config,
    ILogger<ScaleWorker> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>Intervalo de transmissão. 250 ms é imperceptível na balança e mantém o tráfego baixo.</summary>
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(250);

    private int _lastWeight;
    private bool _indicatorOnline;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var readerCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

            try
            {
                using var ws = new ClientWebSocket();

                var url = $"{config["WebSocketUrl"]}?truckScaleId={scaleCode}";
                await ws.ConnectAsync(new Uri(url), stoppingToken);

                logger.LogInformation("Balança {ScaleCode} conectada ao servidor.", scaleCode);

                var scaleConfig = await ReceiveConfigAsync(ws, stoppingToken);

                if (scaleConfig == null)
                    throw new InvalidOperationException("Configuração da balança não recebida.");

                var reader = CreateReader(scaleConfig, readerCts.Token);

                await Task.WhenAny(reader, StreamWeightAsync(ws, readerCts.Token));
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Conexão da balança {ScaleCode} caiu.", scaleCode);
            }
            finally
            {
                await readerCts.CancelAsync();
            }

            try
            {
                await Task.Delay(5000, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private Task CreateReader(ScaleConfigData scaleConfig, CancellationToken ct)
    {
        if (config.GetValue<bool>("UseMockScale"))
        {
            return new Mock.MockScaleReader(w => _lastWeight = w, online => _indicatorOnline = online)
                .RunAsync(ct);
        }

        var connection = new ScaleTcpConnection(
            scaleConfig.Ip ?? "127.0.0.1",
            scaleConfig.Port,
            scaleConfig.ToOptions(),
            scaleConfig.LogRawFrames,
            weight => _lastWeight = weight,
            online => _indicatorOnline = online,
            logger);

        return connection.RunAsync(ct);
    }

    private async Task<ScaleConfigData?> ReceiveConfigAsync(ClientWebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[4096];
        var result = await ws.ReceiveAsync(buffer, ct);
        var json = Encoding.UTF8.GetString(buffer, 0, result.Count);

        var message = JsonSerializer.Deserialize<ScaleConfigMessage>(json, JsonOptions);

        return message?.Action == "scale_config" ? message.Data : null;
    }

    private async Task StreamWeightAsync(ClientWebSocket ws, CancellationToken ct)
    {
        var lastReportedOnline = true;

        while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            if (_indicatorOnline)
            {
                await SendAsync(ws, new { action = "weight_tick", data = new { weight = _lastWeight } }, ct);
                lastReportedOnline = true;
            }
            else if (lastReportedOnline)
            {
                await SendAsync(ws, new { action = "scale_status", data = new { online = false } }, ct);
                lastReportedOnline = false;
            }

            await Task.Delay(TickInterval, ct);
        }
    }

    private static async Task SendAsync(ClientWebSocket ws, object payload, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, JsonOptions));

        await ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
    }
}
