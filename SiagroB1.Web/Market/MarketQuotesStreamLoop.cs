using System.Text.Json;
using System.Text.Json.Serialization;
using SiagroB1.Application.Services.Market;
using SiagroB1.Commons.Market;

namespace SiagroB1.Web.Market;

/// <summary>
/// SSE de cotações, sem HTTP: payload do quadro sobre o laço genérico <see cref="MarketLiveStreamLoop"/>
/// (versão, heartbeat, backoff). Guarda também as constantes e o <see cref="JsonOptions"/> compartilhados.
/// </summary>
public sealed class MarketQuotesStreamLoop(
    LiveQuoteStore store,
    ILogger<MarketQuotesStreamLoop> logger,
    TimeProvider? timeProvider = null,
    Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan RetryBackoff = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan RepeatedLogInterval = TimeSpan.FromMinutes(1);

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public Task RunAsync(
        string companyCode,
        Stream output,
        Func<CancellationToken, Task<IReadOnlyList<MarketQuoteView>>> readBoard,
        CancellationToken cancellationToken) =>
        new MarketLiveStreamLoop(store, logger, timeProvider, delay).RunAsync(companyCode, output, "o quadro de cotações",
            async (snapshot, ct) =>
            {
                var quotes = await readBoard(ct);
                return new { snapshot.Version, snapshot.Status, snapshot.Message, Quotes = quotes };
            }, cancellationToken);
}
