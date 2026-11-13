namespace SiagroB1.Commons.Scales;

/// <summary>
/// Fluxo de leitura que pode ser abandonado. A leitura assíncrona da porta serial não obedece ao
/// token de cancelamento em todas as plataformas, e fechar a porta - que é o que a destrava - pode
/// travar com driver ruim ou adaptador USB removido. Aqui o cancelamento devolve o controle na hora
/// e o fechamento roda em segundo plano, uma única vez.
/// </summary>
/// <param name="inner">Fluxo real (o <c>BaseStream</c> da porta).</param>
/// <param name="close">Fecha e descarta o recurso por trás do fluxo. Pode demorar ou falhar.</param>
public sealed class AbandonOnCancelStream(Stream inner, Action close) : Stream
{
    private int _closed;

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var read = inner.ReadAsync(buffer, CancellationToken.None).AsTask();
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var registration = ct.Register(() => cancelled.TrySetResult());

        if (await Task.WhenAny(read, cancelled.Task) == read)
            return await read;

        // A leitura fica para trás: quando a porta fechar ela falha, e a falha precisa ser observada.
        _ = read.ContinueWith(
            static t => _ = t.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        CloseInBackground();

        throw new OperationCanceledException(ct);
    }

    public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            CloseInBackground();

        base.Dispose(disposing);
    }

    public override ValueTask DisposeAsync()
    {
        CloseInBackground();
        GC.SuppressFinalize(this);

        return ValueTask.CompletedTask;
    }

    private void CloseInBackground()
    {
        if (Interlocked.Exchange(ref _closed, 1) == 1)
            return;

        _ = Task.Run(() =>
        {
            try
            {
                close();
            }
            catch (Exception)
            {
                // Porta já removida do sistema: não há o que fechar.
            }
        });
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
