using System.Text;
using SiagroB1.Commons.Scales;

namespace SiagroB1.Application.Tests.Scales;

public class ScaleStreamReaderTests
{
    private static ScaleProtocolOptions Bj850() => new();

    private static MemoryStream StreamOf(string text) => new(Encoding.ASCII.GetBytes(text));

    [Fact]
    public async Task Emits_one_weight_per_complete_frame()
    {
        var weights = new List<int>();

        await ScaleStreamReader.ReadAsync(
            StreamOf("=012345\r\n=000980\r\n"), Bj850(), weights.Add, null, CancellationToken.None);

        Assert.Equal([12345, 980], weights);
    }

    [Fact]
    public async Task Joins_a_frame_split_across_reads()
    {
        var weights = new List<int>();

        await ScaleStreamReader.ReadAsync(
            new ChunkedStream("=0123", "45\r\n"), Bj850(), weights.Add, null, CancellationToken.None);

        Assert.Equal([12345], weights);
    }

    [Fact]
    public async Task Ignores_garbage_before_the_first_terminator()
    {
        // A serial abre no meio de um quadro: o resto do quadro anterior não pode virar peso.
        var weights = new List<int>();

        await ScaleStreamReader.ReadAsync(
            StreamOf("345\r\n=012345\r\n"), Bj850(), weights.Add, null, CancellationToken.None);

        Assert.Equal([12345], weights);
    }

    [Fact]
    public async Task Does_not_emit_a_frame_without_terminator()
    {
        var weights = new List<int>();

        await ScaleStreamReader.ReadAsync(
            StreamOf("=012345"), Bj850(), weights.Add, null, CancellationToken.None);

        Assert.Empty(weights);
    }

    [Fact]
    public async Task Completes_when_the_stream_ends()
    {
        var task = ScaleStreamReader.ReadAsync(
            StreamOf(""), Bj850(), _ => { }, null, CancellationToken.None);

        await task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Hands_over_each_raw_frame_when_requested()
    {
        var frames = new List<string>();

        await ScaleStreamReader.ReadAsync(
            StreamOf("=012345\r\nlixo\r\n"), Bj850(), _ => { }, frames.Add, CancellationToken.None);

        Assert.Equal(["=012345\r", "lixo\r"], frames);
    }

    [Fact]
    public async Task Cancellation_interrupts_a_silent_stream()
    {
        using var cts = new CancellationTokenSource();
        var task = ScaleStreamReader.ReadAsync(
            new SilentStream(), Bj850(), _ => { }, null, cts.Token);

        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Stream_failure_propagates_to_the_caller()
    {
        // Cabo desconectado: quem chama precisa saber para marcar offline e reconectar.
        await Assert.ThrowsAsync<IOException>(() => ScaleStreamReader.ReadAsync(
            new FailingStream(), Bj850(), _ => { }, null, CancellationToken.None));
    }

    /// <summary>Entrega um pedaço por leitura, como um socket ou uma serial fariam.</summary>
    private sealed class ChunkedStream(params string[] chunks) : ReadOnlyTestStream
    {
        private readonly Queue<byte[]> _chunks = new(chunks.Select(Encoding.ASCII.GetBytes));

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (_chunks.Count == 0)
                return ValueTask.FromResult(0);

            var chunk = _chunks.Dequeue();
            chunk.CopyTo(buffer);

            return ValueTask.FromResult(chunk.Length);
        }
    }

    private sealed class SilentStream : ReadOnlyTestStream
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            await Task.Delay(Timeout.Infinite, ct);
            return 0;
        }
    }

    private sealed class FailingStream : ReadOnlyTestStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) =>
            ValueTask.FromException<int>(new IOException("A porta foi fechada."));
    }

    private abstract class ReadOnlyTestStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
