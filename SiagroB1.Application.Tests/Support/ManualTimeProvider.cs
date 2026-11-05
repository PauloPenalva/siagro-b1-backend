namespace SiagroB1.Application.Tests.Support;

/// <summary>Relógio controlado pelo teste (só o "agora"; atrasos continuam reais ou injetados).</summary>
public sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    private long _ticks = start.UtcTicks;

    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);

    public void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, by.Ticks);
}
