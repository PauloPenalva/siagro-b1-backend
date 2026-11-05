using SiagroB1.Application.Services.Market.Cma;
using SiagroB1.Commons.Market;
using SiagroB1.Infra;
using SiagroB1.Infra.Companies;

namespace SiagroB1.Web.Market;

/// <summary>
/// Mantém uma <see cref="CmaCompanySession"/> por empresa do catálogo (revisto a cada 60 s). A sessão
/// cuida sozinha do estado Desligado (CmaSettings sem credencial). Nenhuma exceção escapa daqui.
/// </summary>
public sealed class CmaStreamingHostedService(
    IConfiguration configuration,
    ICompanyCatalog catalog,
    IServiceProvider services,
    LiveQuoteStore store,
    ILogger<CmaStreamingHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan CatalogInterval = TimeSpan.FromSeconds(60);
    private readonly Dictionary<string, (CancellationTokenSource Cts, Task Task)> _sessions = new(StringComparer.OrdinalIgnoreCase);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!MarketModule.IsEnabled(configuration))
            return;

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await SyncSessionsAsync(stoppingToken);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    logger.LogError(e, "Falha ao sincronizar as sessões da CMA com o catálogo de empresas");
                }

                await Task.Delay(CatalogInterval, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // desligando
        }
        finally
        {
            await StopAllAsync();
        }
    }

    private async Task SyncSessionsAsync(CancellationToken stoppingToken)
    {
        var companies = (await catalog.GetAllAsync(stoppingToken)).Select(c => c.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var code in _sessions.Where(kv => kv.Value.Task.IsCompleted).Select(kv => kv.Key).ToList())
            await StopAsync(code); // sessão que caiu: recomeça abaixo

        foreach (var code in _sessions.Keys.Where(k => !companies.Contains(k)).ToList())
        {
            await StopAsync(code);
            store.SetStatus(code, CmaConnectionStatus.Disabled, null);
        }

        foreach (var code in companies.Where(c => !_sessions.ContainsKey(c)))
        {
            var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            var session = new CmaCompanySession(code,
                services.GetRequiredService<ICmaStreamerClient>(),
                store,
                services.GetRequiredService<ICmaCompanyScope>(),
                services.GetRequiredService<TimeProvider>(),
                services.GetRequiredService<ILogger<CmaCompanySession>>());
            _sessions[code] = (cts, RunSessionAsync(code, session, cts.Token));
        }
    }

    private async Task RunSessionAsync(string code, CmaCompanySession session, CancellationToken token)
    {
        try
        {
            await Task.Yield();
            await session.RunAsync(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception e)
        {
            // A sessão não deveria lançar; se lançar, sai do dicionário e volta no próximo ciclo.
            logger.LogError("Sessão da CMA da empresa {Company} terminou com {ErrorType}", code, e.GetType().Name);
        }
    }

    private async Task StopAsync(string code)
    {
        var (cts, task) = _sessions[code];
        _sessions.Remove(code);
        await cts.CancelAsync();
        await task;
        cts.Dispose();
    }

    private async Task StopAllAsync()
    {
        foreach (var (cts, _) in _sessions.Values)
            await cts.CancelAsync();
        await Task.WhenAll(_sessions.Values.Select(s => s.Task));
        foreach (var (cts, _) in _sessions.Values)
            cts.Dispose();
        _sessions.Clear();
    }
}
