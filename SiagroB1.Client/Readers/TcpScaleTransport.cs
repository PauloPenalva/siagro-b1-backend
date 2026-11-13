using System.Net.Sockets;
using SiagroB1.Commons.Scales;

namespace SiagroB1.Client.Readers;

/// <summary>Indicador (ou conversor serial-ethernet) em rede.</summary>
public sealed class TcpScaleTransport(string host, int port) : IScaleTransport
{
    public string Description => $"{host}:{port}";

    // O socket avisa quando a conexão cai; sem prazo de silêncio, como sempre foi no TCP.
    public TimeSpan? SilenceTimeout => null;

    public async Task<Stream> OpenAsync(CancellationToken ct)
    {
        var client = new TcpClient();

        try
        {
            await client.ConnectAsync(host, port, ct);

            // O NetworkStream de GetStream é dono do socket: descartá-lo fecha a conexão.
            return client.GetStream();
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }
}
