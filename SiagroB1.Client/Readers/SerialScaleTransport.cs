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

            return Task.FromResult<Stream>(new SerialPortStream(port));
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

    /// <summary>
    /// Fluxo de leitura da porta. Fecha a porta ao ser descartado e também quando a leitura é
    /// cancelada: a leitura assíncrona da serial não é interrompida pelo token em todas as
    /// plataformas, e fechar a porta é o que a destrava de fato.
    /// </summary>
    private sealed class SerialPortStream(SerialPort port) : Stream
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            await using var registration = ct.Register(ClosePort);

            try
            {
                return await port.BaseStream.ReadAsync(buffer, ct);
            }
            catch (Exception) when (ct.IsCancellationRequested)
            {
                throw new OperationCanceledException(ct);
            }
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            port.BaseStream.Read(buffer, offset, count);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ClosePort();
                port.Dispose();
            }

            base.Dispose(disposing);
        }

        private void ClosePort()
        {
            try
            {
                if (port.IsOpen)
                    port.Close();
            }
            catch (Exception)
            {
                // Porta já removida do sistema (adaptador USB desconectado): não há o que fechar.
            }
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
