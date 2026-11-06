using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SiagroB1.Commons.Market;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Market.Cma;

/// <summary>Intervalos da sessão; os testes trocam o atraso e o backoff para não dormir.</summary>
public sealed class CmaSessionOptions
{
    public TimeSpan Tick { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan PingInterval { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan InstrumentReloadInterval { get; init; } = TimeSpan.FromSeconds(60);
    public TimeSpan DisabledPollInterval { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>Sem nenhuma mensagem no canal (nem HeartBeat) por mais que isto, a conexão é tida como morta.</summary>
    public TimeSpan ChannelIdleLimit { get; init; } = TimeSpan.FromSeconds(90);

    /// <summary>Conectado por pelo menos isto, o contador de falhas (backoff) volta a zero.</summary>
    public TimeSpan StableConnectionPeriod { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>Repetição de um mesmo log de falha (gravação das cotações) no máximo uma vez neste intervalo.</summary>
    public TimeSpan RepeatedLogInterval { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>Espera após a falha n (0, 1, 2…): 5 s dobrando até 60 s.</summary>
    public Func<int, TimeSpan> Backoff { get; init; } = DefaultBackoff;

    /// <summary>Atraso de um tick; nulo usa o TimeProvider.</summary>
    public Func<TimeSpan, CancellationToken, Task>? Delay { get; init; }

    public static TimeSpan DefaultBackoff(int failure) =>
        TimeSpan.FromSeconds(Math.Min(60, 5 * Math.Pow(2, Math.Min(failure, 4))));
}

/// <summary>
/// Conexão de UMA empresa com a CMA: credenciais → login → canal persistente → assinatura dos instrumentos
/// ativos com sourceId. As cotações são acumuladas e gravadas no banco a cada tick (o banco é a fonte única;
/// o <see cref="LiveQuoteStore"/> só avisa o SSE). Erro → status Error e nova tentativa com backoff; pedido de
/// reconexão → reconecta já com credenciais novas; lista de instrumentos mudou → reassina na mesma sessão.
/// Só termina por cancelamento.
/// </summary>
public sealed class CmaCompanySession(
    string companyCode,
    ICmaStreamerClient client,
    LiveQuoteStore store,
    ICmaCompanyScope scope,
    TimeProvider timeProvider,
    ILogger<CmaCompanySession> logger,
    CmaSessionOptions? options = null)
{
    private readonly CmaSessionOptions _options = options ?? new CmaSessionOptions();
    public const string NoSubscriptionMessage = "Nenhum instrumento com fonte CMA para assinar";

    private readonly CmaQuoteBatch _batch = new();
    private LogThrottle? _flushLogThrottle;
    private LogThrottle FlushLogThrottle => _flushLogThrottle ??= new LogThrottle(_options.RepeatedLogInterval);
    private long _lastChannelActivityTicks;

    private void TouchChannel() => Interlocked.Exchange(ref _lastChannelActivityTicks, timeProvider.GetUtcNow().UtcTicks);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var failures = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var credentials = await scope.RunAsync(companyCode,
                    (sp, ct) => sp.GetRequiredService<CmaSettingsService>().GetCredentialsAsync(ct), cancellationToken);
                if (credentials is null)
                {
                    store.SetStatus(companyCode, CmaConnectionStatus.Disabled, null);
                    await WaitAsync(_options.DisabledPollInterval, cancellationToken);
                    continue;
                }

                await ConnectAndStreamAsync(credentials, () => failures = 0, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                var message = Describe(e);
                store.SetStatus(companyCode, CmaConnectionStatus.Error, message);
                LogFailure(e, message);
                try
                {
                    await WaitAsync(_options.Backoff(failures++), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        await FlushAsync(CancellationToken.None);
    }

    private void LogFailure(Exception e, string message)
    {
        if (e is CmaProtocolException)
            logger.LogWarning("CMA da empresa {Company}: {Error}", companyCode, message);
        else if (CmaFailure.IsTransport(e))
            // Transporte: a Message pode ter a URL (com a senha na query string) — só metadados.
            logger.LogWarning("CMA da empresa {Company}: {Error} ({Details})", companyCode, message, CmaFailure.TransportDetails(e));
        else
            // Banco, cifra, JSON...: nunca viram a URL, que só existe dentro do CmaStreamerClient.
            logger.LogWarning(e, "CMA da empresa {Company}: {Error}", companyCode, message);
    }

    /// <summary>Volta normalmente só quando um pedido de reconexão chega.</summary>
    private async Task ConnectAndStreamAsync(CmaCredentials credentials, Action onStable, CancellationToken cancellationToken)
    {
        store.SetStatus(companyCode, CmaConnectionStatus.Connecting, null);
        var sessionId = await client.LoginAsync(credentials, cancellationToken);

        TouchChannel();
        using var channelCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var channel = PumpChannelAsync(credentials, sessionId, channelCts.Token);
        try
        {
            var symbols = await LoadSymbolsAsync(cancellationToken);
            await SubscribeAsync(credentials, sessionId, symbols, cancellationToken);
            SetConnected(symbols);

            // O backoff só zera depois de um tempo conectado: um servidor que aceita e derruba logo em seguida
            // não pode prender a sessão num reconecta-a-cada-5-s.
            var connectedAt = timeProvider.GetUtcNow();
            var stable = false;
            var lastPing = connectedAt;
            var lastReload = lastPing;
            while (true)
            {
                await DelayAsync(_options.Tick, cancellationToken);

                if (channel.IsCompleted)
                {
                    await channel; // propaga a falha do canal
                    throw new CmaProtocolException("CMA fechou o canal de cotações");
                }

                await FlushAsync(cancellationToken);

                if (store.TryConsumeReconnect(companyCode))
                    return;

                var now = timeProvider.GetUtcNow();
                if (now.UtcTicks - Interlocked.Read(ref _lastChannelActivityTicks) > _options.ChannelIdleLimit.Ticks)
                    throw new CmaProtocolException("CMA sem atividade no canal");

                if (!stable && now - connectedAt >= _options.StableConnectionPeriod)
                {
                    stable = true;
                    onStable();
                }

                if (now - lastPing >= _options.PingInterval)
                {
                    await client.PingAsync(credentials, sessionId, cancellationToken);
                    lastPing = now;
                }

                if (now - lastReload >= _options.InstrumentReloadInterval)
                {
                    lastReload = now;
                    var current = await LoadSymbolsAsync(cancellationToken);
                    if (!current.SetEquals(symbols))
                    {
                        symbols = current;
                        await SubscribeAsync(credentials, sessionId, symbols, cancellationToken);
                        SetConnected(symbols);
                    }
                }
            }
        }
        finally
        {
            await channelCts.CancelAsync();
            try { await channel; }
            catch { /* o canal já foi tratado acima ou foi cancelado aqui */ }
        }
    }

    private async Task PumpChannelAsync(CmaCredentials credentials, string sessionId, CancellationToken cancellationToken)
    {
        await Task.Yield();
        await foreach (var message in client.OpenChannelAsync(credentials, sessionId, cancellationToken))
        {
            TouchChannel();
            if (CmaStreamerClient.IsQuoteUpdate(message) && CmaQuoteMapper.Map(message) is { } update)
                _batch.Add(update);
        }
    }

    private void SetConnected(HashSet<(int? SourceId, string Symbol)> symbols) =>
        store.SetStatus(companyCode, CmaConnectionStatus.Connected, symbols.Count == 0 ? NoSubscriptionMessage : null);

    /// <summary>Lista vazia não vai à CMA (QuotesRequest sem símbolos); o reload de 60 s reconfere.</summary>
    private async Task SubscribeAsync(CmaCredentials credentials, string sessionId,
        HashSet<(int? SourceId, string Symbol)> symbols, CancellationToken cancellationToken)
    {
        if (symbols.Count == 0)
            return;

        var snapshot = await client.SubscribeAsync(credentials, sessionId, symbols, cancellationToken);
        foreach (var quote in snapshot)
            if (CmaQuoteMapper.Map(quote) is { } update)
                _batch.Add(update);
    }

    private Task<HashSet<(int? SourceId, string Symbol)>> LoadSymbolsAsync(CancellationToken cancellationToken) =>
        scope.RunAsync(companyCode, async (sp, ct) =>
        {
            var rows = await sp.GetRequiredService<AppDbContext>().MarketInstruments.AsNoTracking()
                .Where(i => i.Active && i.CmaSourceId != null)
                .Select(i => new { i.CmaSourceId, i.Code })
                .ToListAsync(ct);
            return rows.Select(r => (r.CmaSourceId, r.Code)).ToHashSet();
        }, cancellationToken);

    private async Task FlushAsync(CancellationToken cancellationToken)
    {
        var updates = _batch.TakeAll();
        if (updates.Count == 0)
            return;

        try
        {
            await scope.RunAsync(companyCode, async (sp, ct) =>
            {
                await sp.GetRequiredService<MarketQuoteService>().ApplyCmaUpdatesAsync(updates, ct);
                return true;
            }, cancellationToken);
            store.NotifyChanged(companyCode);
        }
        catch (Exception e)
        {
            _batch.Restore(updates);
            if (e is OperationCanceledException && cancellationToken.IsCancellationRequested)
                throw;
            // Gravação no banco: não passa pela URL da CMA, a exceção vai inteira — uma vez por minuto.
            if (FlushLogThrottle.ShouldLog(timeProvider.GetUtcNow()))
                logger.LogError(e, "Falha ao gravar cotações da CMA na empresa {Company} (repetições omitidas por {Interval} s)",
                    companyCode, _options.RepeatedLogInterval.TotalSeconds);
        }
    }

    /// <summary>Espera o intervalo, interrompida por um pedido de reconexão (verificado a cada tick).</summary>
    private async Task WaitAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        var start = timeProvider.GetUtcNow();
        while (timeProvider.GetUtcNow() - start < interval)
        {
            if (store.TryConsumeReconnect(companyCode))
                return;
            await DelayAsync(_options.Tick, cancellationToken);
        }
    }

    private Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        _options.Delay is { } custom ? custom(delay, cancellationToken) : Task.Delay(delay, timeProvider, cancellationToken);

    /// <summary>Mensagem curta, em pt-BR, sem segredos: nunca a Message de exceção de HTTP (pode ter a URL).</summary>
    internal static string Describe(Exception e) => e switch
    {
        CmaProtocolException p => p.Message,
        BusinessException or DefaultException => e.Message,
        OperationCanceledException => "CMA não respondeu (tempo esgotado)",
        HttpRequestException { StatusCode: { } status } => $"CMA indisponível: HTTP {(int)status}",
        _ when CmaFailure.IsDatabase(e) => CmaFailure.DatabaseMessage,
        _ => $"CMA indisponível: {e.GetType().Name}",
    };
}
