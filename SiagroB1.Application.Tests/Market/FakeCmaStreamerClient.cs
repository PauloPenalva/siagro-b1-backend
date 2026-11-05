using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using SiagroB1.Application.Services.Market;
using SiagroB1.Application.Services.Market.Cma;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Market;
using SiagroB1.Infra.Companies;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.Market;

/// <summary>Streamer da CMA roteirizado: logins que falham, canal alimentado pelo teste, assinaturas gravadas.</summary>
internal sealed class FakeCmaStreamerClient : ICmaStreamerClient
{
    private readonly object _gate = new();
    private Channel<JsonElement> _channel = Channel.CreateUnbounded<JsonElement>();

    public Queue<Exception> LoginFailures { get; } = new();
    public int LoginCalls { get; private set; }
    public List<CmaCredentials> LoginCredentials { get; } = [];
    public List<IReadOnlyCollection<(int? SourceId, string Symbol)>> Subscriptions { get; } = [];

    public int SubscriptionCount { get { lock (_gate) return Subscriptions.Count; } }

    public Task<string> LoginAsync(CmaCredentials credentials, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            LoginCalls++;
            LoginCredentials.Add(credentials);
            if (LoginFailures.TryDequeue(out var failure))
                throw failure;
            _channel = Channel.CreateUnbounded<JsonElement>();
            return Task.FromResult($"session-{LoginCalls}");
        }
    }

    public async IAsyncEnumerable<JsonElement> OpenChannelAsync(CmaCredentials credentials, string sessionId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Channel<JsonElement> channel;
        lock (_gate) channel = _channel;
        await foreach (var item in channel.Reader.ReadAllAsync(cancellationToken))
            yield return item;
    }

    public Task<IReadOnlyList<JsonElement>> SubscribeAsync(CmaCredentials credentials, string sessionId,
        IReadOnlyCollection<(int? SourceId, string Symbol)> symbols, CancellationToken cancellationToken)
    {
        lock (_gate) Subscriptions.Add(symbols.ToList());
        return Task.FromResult<IReadOnlyList<JsonElement>>([]);
    }

    public Task PingAsync(CmaCredentials credentials, string sessionId, CancellationToken cancellationToken) => Task.CompletedTask;

    public void Deliver(string json)
    {
        Channel<JsonElement> channel;
        lock (_gate) channel = _channel;
        channel.Writer.TryWrite(JsonDocument.Parse(json).RootElement.Clone());
    }
}

/// <summary>Escopo de empresa sobre uma base InMemory nomeada: contexto novo a cada chamada, como em produção.</summary>
internal sealed class FakeCmaCompanyScope(string databaseName, LiveQuoteStore store, NfeOptions nfeOptions) : ICmaCompanyScope
{
    public async Task<T> RunAsync<T>(string companyCode, Func<IServiceProvider, CancellationToken, Task<T>> work, CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => new AppDbContext(TestDb.Options(databaseName)));
        services.AddScoped<ICompanyContext>(_ => new FakeCompanyContext(companyCode));
        services.AddScoped(sp => new CmaSettingsService(sp.GetRequiredService<AppDbContext>(), nfeOptions));
        services.AddScoped(sp => new MarketQuoteService(sp.GetRequiredService<AppDbContext>(), store,
            sp.GetRequiredService<ICompanyContext>(), new TestLogger<MarketQuoteService>()));
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        return await work(scope.ServiceProvider, cancellationToken);
    }
}
