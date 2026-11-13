using SiagroB1.Commons.Scales;

namespace SiagroB1.Application.Tests.Scales;

public class AbandonOnCancelStreamTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(2);

    [Fact]
    public async Task Passes_through_the_bytes_of_the_inner_stream()
    {
        var inner = new MemoryStream([1, 2, 3]);
        await using var stream = new AbandonOnCancelStream(inner, () => { });
        var buffer = new byte[8];

        var read = await stream.ReadAsync(buffer, CancellationToken.None);

        Assert.Equal(3, read);
        Assert.Equal([1, 2, 3], buffer[..3]);
    }

    [Fact]
    public async Task Cancellation_interrupts_a_read_that_ignores_the_token()
    {
        // A leitura da porta serial não obedece ao token em todas as plataformas.
        var stream = new AbandonOnCancelStream(new DeafStream(), () => { });
        using var cts = new CancellationTokenSource();

        var read = stream.ReadAsync(new byte[8], cts.Token).AsTask();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read.WaitAsync(Patience));
    }

    [Fact]
    public async Task Cancellation_does_not_wait_for_a_close_that_hangs()
    {
        // Driver ruim ou adaptador USB removido: fechar a porta pode travar, e o worker da balança
        // não pode ficar preso junto.
        using var release = new ManualResetEventSlim();
        using var closing = new ManualResetEventSlim();
        var stream = new AbandonOnCancelStream(new DeafStream(), () =>
        {
            closing.Set();
            release.Wait();
        });
        using var cts = new CancellationTokenSource();

        var read = stream.ReadAsync(new byte[8], cts.Token).AsTask();
        var cancel = cts.CancelAsync();

        await cancel.WaitAsync(Patience);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read.WaitAsync(Patience));
        Assert.True(closing.Wait(Patience), "o fechamento precisa ser disparado");

        release.Set();
    }

    [Fact]
    public async Task Dispose_does_not_wait_for_a_close_that_hangs()
    {
        using var release = new ManualResetEventSlim();
        using var closing = new ManualResetEventSlim();
        var stream = new AbandonOnCancelStream(new DeafStream(), () =>
        {
            closing.Set();
            release.Wait();
        });

        await stream.DisposeAsync().AsTask().WaitAsync(Patience);

        Assert.True(closing.Wait(Patience), "o fechamento precisa ser disparado");

        release.Set();
    }

    [Fact]
    public async Task Close_runs_only_once()
    {
        var closes = 0;
        using var closed = new ManualResetEventSlim();
        var stream = new AbandonOnCancelStream(new DeafStream(), () =>
        {
            Interlocked.Increment(ref closes);
            closed.Set();
        });
        using var cts = new CancellationTokenSource();

        var read = stream.ReadAsync(new byte[8], cts.Token).AsTask();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read.WaitAsync(Patience));
        await stream.DisposeAsync();

        Assert.True(closed.Wait(Patience));
        await Task.Delay(100);
        Assert.Equal(1, Volatile.Read(ref closes));
    }

    [Fact]
    public async Task Inner_failure_propagates_when_not_cancelled()
    {
        await using var stream = new AbandonOnCancelStream(new FailingStream(), () => { });

        await Assert.ThrowsAsync<IOException>(
            () => stream.ReadAsync(new byte[8], CancellationToken.None).AsTask());
    }

    /// <summary>Leitura que nunca termina e ignora o token, como a serial em algumas plataformas.</summary>
    private sealed class DeafStream : TestStream
    {
        private readonly TaskCompletionSource<int> _never = new();

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) =>
            new(_never.Task);
    }

    private sealed class FailingStream : TestStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) =>
            ValueTask.FromException<int>(new IOException("A porta foi fechada."));
    }

    private abstract class TestStream : Stream
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
