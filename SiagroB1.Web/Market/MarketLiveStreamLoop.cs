using System.Text;
using System.Text.Json;
using SiagroB1.Commons.Market;

namespace SiagroB1.Web.Market;

/// <summary>
/// Núcleo genérico dos SSE do Mercado, sem HTTP: confere a versão do store a cada tick e, quando muda,
/// monta o payload e escreve o evento. Falha ao montar não derruba o stream: registra, espera o backoff e
/// tenta de novo (a versão só avança depois de uma emissão bem-sucedida). O heartbeat segue durante o backoff.
/// </summary>
public sealed class MarketLiveStreamLoop(
    LiveQuoteStore store,
    ILogger logger,
    TimeProvider? timeProvider = null,
    Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;

    /// <param name="streamName">Nome da fonte para o log (ex.: "quadro de cotações").</param>
    /// <param name="buildPayload">Monta o objeto serializado no evento; recebe o snapshot da versão sendo emitida.</param>
    /// <param name="minInterval">Intervalo mínimo entre dois eventos de dados (nulo = sem limite, o padrão).</param>
    public async Task RunAsync(
        string companyCode,
        Stream output,
        string streamName,
        Func<LiveQuoteSnapshot, CancellationToken, Task<object>> buildPayload,
        CancellationToken cancellationToken,
        TimeSpan? minInterval = null)
    {
        var lastVersion = -1L;
        DateTimeOffset? lastEmit = null;
        var lastWrite = _time.GetUtcNow();
        DateTimeOffset? retryAt = null;
        var readFailureLog = new LogThrottle(MarketQuotesStreamLoop.RepeatedLogInterval);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var now = _time.GetUtcNow();
                var snapshot = store.Get(companyCode);
                var emit = snapshot.Version != lastVersion && (retryAt is null || now >= retryAt)
                           && (minInterval is null || lastEmit is null || now - lastEmit >= minInterval);

                if (emit)
                {
                    object? payload = null;
                    try
                    {
                        payload = await buildPayload(snapshot, cancellationToken);
                    }
                    catch (Exception e) when (!cancellationToken.IsCancellationRequested)
                    {
                        // Sem payload no log; a próxima tentativa só acontece após o backoff. Uma linha por minuto.
                        if (readFailureLog.ShouldLog(now))
                            logger.LogWarning(e, "Falha ao ler {Stream} da empresa {Company}; nova tentativa em {Backoff} s.",
                                streamName, companyCode, MarketQuotesStreamLoop.RetryBackoff.TotalSeconds);
                        retryAt = now + MarketQuotesStreamLoop.RetryBackoff;
                    }

                    if (payload is not null)
                    {
                        await WriteAsync(output, $"data: {JsonSerializer.Serialize(payload, MarketQuotesStreamLoop.JsonOptions)}\n\n", cancellationToken);
                        lastVersion = snapshot.Version;
                        retryAt = null;
                        lastWrite = _time.GetUtcNow();
                        lastEmit = now;
                    }
                }
                else if (now - lastWrite >= MarketQuotesStreamLoop.HeartbeatInterval)
                {
                    await WriteAsync(output, ": ping\n\n", cancellationToken);
                    lastWrite = _time.GetUtcNow();
                }

                await _delay(MarketQuotesStreamLoop.PollInterval, cancellationToken);
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
