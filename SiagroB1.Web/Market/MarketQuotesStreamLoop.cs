using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SiagroB1.Application.Services.Market;
using SiagroB1.Commons.Market;

namespace SiagroB1.Web.Market;

/// <summary>
/// Núcleo do SSE de cotações, sem HTTP: confere a versão do store a cada tick e, quando muda, relê
/// o quadro e escreve o evento. Falha de leitura não derruba o stream: registra, espera o backoff e
/// tenta de novo (a versão só avança depois de uma emissão bem-sucedida). O heartbeat segue durante o backoff.
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

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;

    public async Task RunAsync(
        string companyCode,
        Stream output,
        Func<CancellationToken, Task<IReadOnlyList<MarketQuoteView>>> readBoard,
        CancellationToken cancellationToken)
    {
        var lastVersion = -1L;
        var lastWrite = _time.GetUtcNow();
        DateTimeOffset? retryAt = null;
        var readFailureLog = new LogThrottle(RepeatedLogInterval);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var now = _time.GetUtcNow();
                var snapshot = store.Get(companyCode);
                var emit = snapshot.Version != lastVersion && (retryAt is null || now >= retryAt);

                if (emit)
                {
                    IReadOnlyList<MarketQuoteView>? quotes = null;
                    try
                    {
                        quotes = await readBoard(cancellationToken);
                    }
                    catch (Exception e) when (!cancellationToken.IsCancellationRequested)
                    {
                        // Sem payload no log; a próxima tentativa só acontece após o backoff. Uma linha por minuto.
                        if (readFailureLog.ShouldLog(now))
                            logger.LogWarning(e, "Falha ao ler o quadro de cotações da empresa {Company}; nova tentativa em {Backoff} s.",
                            companyCode, RetryBackoff.TotalSeconds);
                        retryAt = now + RetryBackoff;
                    }

                    if (quotes is not null)
                    {
                        var payload = new { snapshot.Version, snapshot.Status, snapshot.Message, Quotes = quotes };
                        await WriteAsync(output, $"data: {JsonSerializer.Serialize(payload, JsonOptions)}\n\n", cancellationToken);
                        lastVersion = snapshot.Version;
                        retryAt = null;
                        lastWrite = _time.GetUtcNow();
                    }
                }
                else if (now - lastWrite >= HeartbeatInterval)
                {
                    await WriteAsync(output, ": ping\n\n", cancellationToken);
                    lastWrite = _time.GetUtcNow();
                }

                await _delay(PollInterval, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Navegador fechou a conexão (RequestAborted): encerramento normal.
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
            // Cliente desconectado durante a escrita.
        }
    }

    private static async Task WriteAsync(Stream output, string text, CancellationToken cancellationToken)
    {
        await output.WriteAsync(Encoding.UTF8.GetBytes(text), cancellationToken);
        await output.FlushAsync(cancellationToken);
    }
}
