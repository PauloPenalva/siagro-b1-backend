using System.Text;

namespace SiagroB1.Commons.Scales;

/// <summary>
/// Lê o indicador por um fluxo de bytes, monta os quadros e devolve cada peso pelo callback. Não
/// sabe de onde o fluxo vem: socket TCP e porta serial entregam os mesmos quadros.
/// </summary>
public static class ScaleStreamReader
{
    /// <summary>
    /// Lê até o fluxo terminar ou o token cancelar. Falha do fluxo (cabo desconectado, porta
    /// fechada) propaga: quem chama marca a balança como offline e reconecta.
    /// </summary>
    public static async Task ReadAsync(
        Stream stream,
        ScaleProtocolOptions options,
        Action<int> onWeight,
        Action<string>? onRawFrame,
        CancellationToken ct)
    {
        var protocol = ScaleProtocolFactory.Create(options);
        var buffer = new ScaleFrameBuffer(options.FrameTerminator);
        var bytes = new byte[1024];

        while (!ct.IsCancellationRequested)
        {
            var read = await stream.ReadAsync(bytes, ct);

            if (read == 0)
                break;

            var chunk = Encoding.ASCII.GetString(bytes, 0, read);

            foreach (var frame in buffer.Append(chunk))
            {
                onRawFrame?.Invoke(frame);

                if (protocol.TryParse(frame, out var weight))
                    onWeight(weight);
            }
        }
    }
}
