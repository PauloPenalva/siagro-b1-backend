using System.Text;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.Market;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Market;
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
}
