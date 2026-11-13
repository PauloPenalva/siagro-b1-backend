using System.Collections.Concurrent;
using System.Text;
using System.Threading.Channels;
using SiagroB1.Commons.Scales;

namespace SiagroB1.Application.Tests.Scales;

public class ScaleIndicatorConnectionTests
{
    private static readonly TimeSpan Retry = TimeSpan.FromMilliseconds(10);
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Reports_online_only_after_the_first_valid_weight()
    {
        // Porta serial abre mesmo sem indicador ligado: aberta não é online.
        var stream = new FeedStream();
        var transport = new FakeTransport(_ => stream);
        var events = new EventLog();
        using var cts = new CancellationTokenSource();
        var run = Connection(transport, events).RunAsync(cts.Token);

        await transport.WaitForOpens(1);
        stream.Feed("lixo\r\n");
        await Task.Delay(100);
        Assert.DoesNotContain("online", events.Snapshot());

        stream.Feed("=012345\r\n");
        await events.WaitFor("online");

        Assert.Equal(["weight:12345", "online"], events.Snapshot());

        await cts.CancelAsync();
        await run.WaitAsync(Patience);
    }

    [Fact]
    public async Task Stream_end_reports_offline_and_reopens()
    {
        var first = new FeedStream();
        var transport = new FakeTransport(attempt => attempt == 1 ? first : new FeedStream());
        var events = new EventLog();
        using var cts = new CancellationTokenSource();
        var run = Connection(transport, events).RunAsync(cts.Token);

        first.Feed("=012345\r\n");
        await events.WaitFor("online");
        first.Complete();

        await events.WaitFor("offline");
        await transport.WaitForOpens(2);

        await cts.CancelAsync();
        await run.WaitAsync(Patience);
    }

    [Fact]
    public async Task Open_failure_reports_offline_and_keeps_retrying()
    {
        // Porta inexistente ou ocupada por outro programa: o Client não pode cair.
        var transport = new FakeTransport(_ => throw new IOException("A porta COM9 não existe."));
        var events = new EventLog();
        using var cts = new CancellationTokenSource();
        var run = Connection(transport, events).RunAsync(cts.Token);

        await transport.WaitForOpens(3);

        Assert.Contains("offline", events.Snapshot());
        Assert.DoesNotContain("online", events.Snapshot());
        Assert.False(run.IsCompleted);

        await cts.CancelAsync();
        await run.WaitAsync(Patience);
    }

    [Fact]
    public async Task Stream_failure_while_reading_reports_offline_and_reopens()
    {
        var first = new FeedStream();
        var transport = new FakeTransport(attempt => attempt == 1 ? first : new FeedStream());
        var events = new EventLog();
        using var cts = new CancellationTokenSource();
        var run = Connection(transport, events).RunAsync(cts.Token);

        first.Feed("=012345\r\n");
        await events.WaitFor("online");
        first.Fail(new IOException("Cabo desconectado."));

        await events.WaitFor("offline");
        await transport.WaitForOpens(2);

        await cts.CancelAsync();
        await run.WaitAsync(Patience);
    }

    [Fact]
    public async Task Silence_beyond_the_transport_timeout_reports_offline()
    {
        // Serial não avisa que o cabo saiu: sem quadro válido no prazo, o peso antigo não vale mais.
        var first = new FeedStream();
        var transport = new FakeTransport(attempt => attempt == 1 ? first : new FeedStream())
        {
            SilenceTimeout = TimeSpan.FromMilliseconds(150)
        };
        var events = new EventLog();
        using var cts = new CancellationTokenSource();
        var run = Connection(transport, events).RunAsync(cts.Token);

        first.Feed("=012345\r\n");
        await events.WaitFor("online");

        await events.WaitFor("offline");
        await transport.WaitForOpens(2);

        await cts.CancelAsync();
        await run.WaitAsync(Patience);
    }

    [Fact]
    public async Task Garbage_does_not_postpone_the_silence_timeout()
    {
        // Baud rate errado entrega bytes sem parar, mas nenhum peso: continua sendo silêncio.
        var first = new FeedStream();
        var transport = new FakeTransport(attempt => attempt == 1 ? first : new FeedStream())
        {
            SilenceTimeout = TimeSpan.FromMilliseconds(200)
        };
        var events = new EventLog();
        using var cts = new CancellationTokenSource();
        var run = Connection(transport, events).RunAsync(cts.Token);

        first.Feed("=012345\r\n");
        await events.WaitFor("online");

        using var feeding = new CancellationTokenSource();
        var feeder = Task.Run(async () =>
        {
            while (!feeding.IsCancellationRequested)
            {
                first.Feed("lixo\r\n");
                await Task.Delay(40);
            }
        });

        await events.WaitFor("offline");
        await feeding.CancelAsync();
        await feeder;

        await cts.CancelAsync();
        await run.WaitAsync(Patience);
    }

    [Fact]
    public async Task Transport_without_silence_timeout_stays_online_while_silent()
    {
        var stream = new FeedStream();
        var transport = new FakeTransport(_ => stream);
        var events = new EventLog();
        using var cts = new CancellationTokenSource();
        var run = Connection(transport, events).RunAsync(cts.Token);

        stream.Feed("=012345\r\n");
        await events.WaitFor("online");
        await Task.Delay(400);

        Assert.DoesNotContain("offline", events.Snapshot());

        await cts.CancelAsync();
        await run.WaitAsync(Patience);
    }

    [Fact]
    public async Task Hands_over_raw_frames_when_requested()
    {
        var stream = new FeedStream();
        var transport = new FakeTransport(_ => stream);
        var events = new EventLog();
        var frames = new ConcurrentQueue<string>();
        using var cts = new CancellationTokenSource();
        var run = new ScaleIndicatorConnection(
            transport, new ScaleProtocolOptions(), events.Weight, events.Online, frames.Enqueue,
            (_, _) => { }, Retry).RunAsync(cts.Token);

        stream.Feed("=012345\r\n");
        await events.WaitFor("online");

        Assert.Equal(["=012345\r"], frames);

        await cts.CancelAsync();
        await run.WaitAsync(Patience);
    }

    private static ScaleIndicatorConnection Connection(IScaleTransport transport, EventLog events) =>
        new(transport, new ScaleProtocolOptions(), events.Weight, events.Online, null, (_, _) => { }, Retry);

    private sealed class EventLog
    {
        private readonly List<string> _events = [];

        public void Weight(int weight) => Add($"weight:{weight}");

        public void Online(bool online) => Add(online ? "online" : "offline");

        public string[] Snapshot()
        {
            lock (_events)
                return [.. _events];
        }

        public async Task WaitFor(string expected)
        {
            var deadline = DateTime.UtcNow + Patience;

            while (!Snapshot().Contains(expected))
            {
                if (DateTime.UtcNow > deadline)
                    throw new TimeoutException($"{expected} não chegou. Eventos: {string.Join(", ", Snapshot())}");

                await Task.Delay(10);
            }
        }

        private void Add(string entry)
        {
            lock (_events)
                _events.Add(entry);
        }
    }

    private sealed class FakeTransport(Func<int, Stream> open) : IScaleTransport
    {
        private int _opens;

        public string Description => "fake";

        public TimeSpan? SilenceTimeout { get; init; }

        public Task<Stream> OpenAsync(CancellationToken ct)
        {
            var attempt = Interlocked.Increment(ref _opens);
            return Task.FromResult(open(attempt));
        }

        public async Task WaitForOpens(int count)
        {
            var deadline = DateTime.UtcNow + Patience;

            while (Volatile.Read(ref _opens) < count)
            {
                if (DateTime.UtcNow > deadline)
                    throw new TimeoutException($"Só {_opens} abertura(s); esperava {count}.");

                await Task.Delay(10);
            }
        }
    }

    /// <summary>Fluxo alimentado pelo teste: dados, fim ou falha, na hora em que o teste decidir.</summary>
    private sealed class FeedStream : Stream
    {
        private readonly Channel<byte[]> _channel = Channel.CreateUnbounded<byte[]>();

        public void Feed(string text) => _channel.Writer.TryWrite(Encoding.ASCII.GetBytes(text));

        public void Complete() => _channel.Writer.TryComplete();

        public void Fail(Exception error) => _channel.Writer.TryComplete(error);

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            try
            {
                var chunk = await _channel.Reader.ReadAsync(ct);
                chunk.CopyTo(buffer);
                return chunk.Length;
            }
            catch (ChannelClosedException ex) when (ex.InnerException is null)
            {
                return 0;
            }
            catch (ChannelClosedException ex)
            {
                throw ex.InnerException!;
            }
        }

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
