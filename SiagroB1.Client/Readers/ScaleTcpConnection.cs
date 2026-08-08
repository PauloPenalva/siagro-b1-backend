using System.Net.Sockets;
using System.Text;
using SiagroB1.Commons.Scales;

namespace SiagroB1.Client.Readers;

/// <summary>
/// Mantém o socket com o indicador e devolve cada peso lido pelo callback. Reconecta sozinho: a
/// queda do indicador não pode derrubar a conexão com o servidor.
/// </summary>
public sealed class ScaleTcpConnection(
    string host,
    int port,
    ScaleProtocolOptions options,
    bool logRawFrames,
    Action<int> onWeight,
    Action<bool> onConnectionChanged,
    ILogger logger)
{
    public async Task RunAsync(CancellationToken ct)
    {
        var protocol = ScaleProtocolFactory.Create(options);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(host, port, ct);

                onConnectionChanged(true);
                logger.LogInformation("Indicador {Host}:{Port} conectado.", host, port);

                var stream = client.GetStream();
                var bytes = new byte[1024];
                var buffer = new ScaleFrameBuffer(options.FrameTerminator);

                while (!ct.IsCancellationRequested && client.Connected)
                {
                    var read = await stream.ReadAsync(bytes, ct);
                    if (read == 0)
                        break;

                    var chunk = Encoding.ASCII.GetString(bytes, 0, read);

                    foreach (var frame in buffer.Append(chunk))
                    {
                        if (logRawFrames)
                            logger.LogInformation("Frame cru: {Frame}", frame);

                        if (protocol.TryParse(frame, out var weight))
                            onWeight(weight);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Falha no indicador {Host}:{Port}.", host, port);
            }

            onConnectionChanged(false);

            try
            {
                await Task.Delay(3000, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
