using System.IO.Ports;
using SiagroB1.Commons.Scales;

namespace SiagroB1.Client.Readers;

/// <summary>Indicador ligado direto na porta serial do PC da balança.</summary>
public sealed class SerialScaleTransport(
    string portName,
    int baudRate,
    int dataBits,
    string? parity,
    int stopBits) : IScaleTransport
{
    public string Description => $"{portName} {baudRate} {dataBits}{ParityLetter}{stopBits}";

    // A serial não avisa quando o cabo sai: sem peso válido nesse prazo, a balança vai a offline.
    public TimeSpan? SilenceTimeout => TimeSpan.FromSeconds(5);

    public Task<Stream> OpenAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var port = new SerialPort(
            portName,
            baudRate,
            ParseParity(parity),
            dataBits,
            stopBits == 2 ? StopBits.Two : StopBits.One)
        {
            Handshake = Handshake.None,
            ReadTimeout = SerialPort.InfiniteTimeout
        };

        try
        {
            port.Open();

            // Fechar a porta é o que interrompe a leitura, e pode travar: fica em segundo plano.
            return Task.FromResult<Stream>(new AbandonOnCancelStream(port.BaseStream, () =>
            {
                try
                {
                    if (port.IsOpen)
                        port.Close();
                }
                finally
                {
                    port.Dispose();
                }
            }));
        }
        catch
        {
            port.Dispose();
            throw;
        }
    }

    private char ParityLetter => ParseParity(parity) switch
    {
        Parity.Odd => 'O',
        Parity.Even => 'E',
        _ => 'N'
    };

    private static Parity ParseParity(string? value) =>
        Enum.TryParse<Parity>(value, ignoreCase: true, out var parsed) && parsed is Parity.Odd or Parity.Even
            ? parsed
            : Parity.None;
}
