using System.Text;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.Market;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Market;
using SiagroB1.Domain.Enums;
using SiagroB1.Web.Market;

namespace SiagroB1.Application.Tests.Market;

public class MarketQuotesStreamLoopTests
{
    private sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static MarketQuoteView View(string code) =>
        new(code, code, "G", 1, 10m, null, null, null, null, null, false, null, null, false);

    [Fact]
    public async Task Sse_BoardReadFails_KeepsStreamAndRetries()
    {
        var store = new LiveQuoteStore();
        store.NotifyChanged("A");
        var time = new ManualTime();
        var logger = new TestLogger<MarketQuotesStreamLoop>();
        using var cts = new CancellationTokenSource();
        var output = new MemoryStream();
        var reads = 0;
        var ticks = 0;
        var loop = new MarketQuotesStreamLoop(store, logger, time, (span, _) =>
        {
            time.Now += span;
            // 14 ticks de 500 ms = 7 s: passa do backoff de 5 s; depois encerra.
            if (++ticks >= 14) cts.Cancel();
            return Task.CompletedTask;
        });

        await loop.RunAsync("A", output, _ =>
        {
            reads++;
            if (reads == 1) throw new InvalidOperationException("db fora");
            return Task.FromResult<IReadOnlyList<MarketQuoteView>>([View("ZSF7")]);
        }, cts.Token);

        var text = Encoding.UTF8.GetString(output.ToArray());
        Assert.Equal(2, reads);
        Assert.Equal(1, text.Split("data: ").Length - 1);
        Assert.Contains("ZSF7", text);
        Assert.Contains("\"version\":1", text);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains('A'));
        Assert.DoesNotContain("ZSF7", string.Join('\n', logger.Entries.Select(e => e.Message)));
    }

    [Fact]
    public async Task Sse_Cancellation_EndsQuietly()
    {
        var store = new LiveQuoteStore();
        using var cts = new CancellationTokenSource();
        var loop = new MarketQuotesStreamLoop(store, new TestLogger<MarketQuotesStreamLoop>(), new ManualTime(), (_, ct) =>
        {
            cts.Cancel();
            ct.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        });

        await loop.RunAsync("A", new MemoryStream(), _ => Task.FromResult<IReadOnlyList<MarketQuoteView>>([]), cts.Token);

        Assert.True(cts.IsCancellationRequested);
    }
    [Fact]
    public async Task Sse_RepeatedReadFailures_LoggedAtMostOncePerMinute()
    {
        var store = new LiveQuoteStore();
        store.NotifyChanged("A");
        var time = new ManualTime();
        var logger = new TestLogger<MarketQuotesStreamLoop>();
        using var cts = new CancellationTokenSource();
        var ticks = 0;
        var reads = 0;
        var loop = new MarketQuotesStreamLoop(store, logger, time, (span, _) =>
        {
            time.Now += span;
            // 150 ticks de 500 ms = 75 s: ~15 tentativas (backoff de 5 s), cruzando a marca de 1 minuto uma vez.
            if (++ticks >= 150) cts.Cancel();
            return Task.CompletedTask;
        });

        await loop.RunAsync("A", new MemoryStream(), _ =>
        {
            reads++;
            throw new InvalidOperationException("db fora");
        }, cts.Token);

        Assert.True(reads > 10);
        Assert.Equal(2, logger.Entries.Count(e => e.Level == LogLevel.Warning));
    }

    [Fact]
    public async Task GenericLoop_EmitsPayloadOnVersionChange_AndHeartbeats()
    {
        var store = new LiveQuoteStore();
        store.NotifyChanged("A");
        var time = new ManualTime();
        using var cts = new CancellationTokenSource();
        var output = new MemoryStream();
        var ticks = 0;
        var builds = 0;
        var loop = new MarketLiveStreamLoop(store, new TestLogger<MarketQuotesStreamLoop>(), time, (span, _) =>
        {
            time.Now += span;
            // Muda a versão no 4o tick; 40 ticks (20 s) cobrem um heartbeat de 15 s.
            if (++ticks == 4) store.NotifyChanged("A");
            if (ticks >= 40) cts.Cancel();
            return Task.CompletedTask;
        });

        await loop.RunAsync("A", output, "teste", (snapshot, _) =>
        {
            builds++;
            return Task.FromResult<object>(new { snapshot.Version, Items = new[] { MarketPriceStatus.MissingFx } });
        }, cts.Token);

        var text = Encoding.UTF8.GetString(output.ToArray());
        Assert.Equal(2, builds);
        Assert.Equal(2, text.Split("data: ").Length - 1);
        Assert.Contains("\"version\":2", text);
        Assert.Contains("\"missingFx\"", text);
        Assert.Contains(": ping", text);
    }
}
