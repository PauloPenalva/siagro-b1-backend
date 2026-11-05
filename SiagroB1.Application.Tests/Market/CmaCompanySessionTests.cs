using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Market;
using SiagroB1.Application.Services.Market.Cma;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Market;
using SiagroB1.Domain.Entities.Market;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.Market;

public class CmaCompanySessionTests
{
    private const string Company = "0001";
    private const string Password = "segredo-cma";
    private static readonly string Key = Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());

    private sealed class Harness : IAsyncDisposable
    {
        public string DbName { get; } = Guid.NewGuid().ToString();
        public LiveQuoteStore Store { get; } = new();
        public FakeCmaStreamerClient Client { get; } = new();
        public TestLogger<CmaCompanySession> Logger { get; } = new();
        public NfeOptions Nfe { get; } = new(TestConfiguration.With(("Nfe:CertificateKey", Key)));
        private readonly CancellationTokenSource _cts = new();
        private Task? _run;

        public AppDbContext NewContext() => new(TestDb.Options(DbName));

        public async Task SeedAsync(bool enabled = true, params MarketInstrument[] instruments)
        {
            await using var context = NewContext();
            context.MarketInstruments.AddRange(instruments);
            await context.SaveChangesAsync();
            await new CmaSettingsService(context, Nfe).SaveAsync(new CmaSettingsInput(enabled, "https://feed.exemplo.com:443", "usuario", Password));
        }

        public void Start(TimeSpan? reload = null, TimeSpan? backoff = null, TimeProvider? time = null) =>
            _run = new CmaCompanySession(Company, Client, Store, new FakeCmaCompanyScope(DbName, Store, Nfe), time ?? TimeProvider.System, Logger,
                new CmaSessionOptions
                {
                    Delay = (_, ct) => Task.Delay(5, ct),
                    ChannelIdleLimit = TimeSpan.FromSeconds(90),
                    Tick = TimeSpan.FromMilliseconds(5),
                    PingInterval = TimeSpan.FromHours(1),
                    InstrumentReloadInterval = reload ?? TimeSpan.FromHours(1),
                    DisabledPollInterval = TimeSpan.FromMilliseconds(20),
                    Backoff = _ => backoff ?? TimeSpan.Zero,
                }).RunAsync(_cts.Token);

        public async ValueTask DisposeAsync()
        {
            await _cts.CancelAsync();
            if (_run is not null)
                await _run.WaitAsync(TimeSpan.FromSeconds(10));
            _cts.Dispose();
        }
    }

    private static MarketInstrument Instrument(string code, int? sourceId, bool active = true)
    {
        var i = MarketTestHelpers.Instrument(code, active);
        i.CmaSourceId = sourceId;
        return i;
    }

    private static string Quote(string symbol, string last) =>
        $$"""{"name":"QuotesUpdateResponse","datetime":"2026-11-05 10:00:00.000","symbolId":{"sourceId":"30","symbol":"{{symbol}}"},"arrValues":[{"10":"{{last}}"}]}""";

    /// <summary>Espera uma condição (sem dormir um tempo fixo); falha após 10 s.</summary>
    private static Task WaitUntil(Func<bool> condition, string what) => WaitUntil(() => Task.FromResult(condition()), what);

    private static async Task WaitUntil(Func<Task<bool>> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"Condição não atingida: {what}");
            await Task.Delay(5);
        }
    }

    private static async Task<bool> QuotesExist(Harness h, params string[] symbols)
    {
        await using var context = h.NewContext();
        return await context.MarketQuotes.CountAsync(q => symbols.Contains(q.InstrumentCode) && q.Last != null) == symbols.Length;
    }

    [Fact]
    public async Task Session_FlushesMappedQuotes_AndNotifies()
    {
        await using var h = new Harness();
        await h.SeedAsync(true, Instrument("ZSF7", 30), Instrument("ZCH7", 30));
        h.Start();

        await WaitUntil(() => h.Store.Get(Company).Status == CmaConnectionStatus.Connected, "conectado");
        h.Client.Deliver(Quote("ZSF7", "1188.25"));
        h.Client.Deliver(Quote("ZCH7", "450.5S"));

        await WaitUntil(() => QuotesExist(h, "ZSF7", "ZCH7"), "cotações no banco");

        await using var context = h.NewContext();
        var soy = await context.MarketQuotes.SingleAsync(q => q.InstrumentCode == "ZSF7");
        Assert.Equal(1188.25m, soy.Last);
        Assert.Equal(MarketQuoteSource.Cma, soy.Source);
        Assert.Equal(450.5m, (await context.MarketQuotes.SingleAsync(q => q.InstrumentCode == "ZCH7")).Last);
        Assert.True(h.Store.Get(Company).Version > 0);
        Assert.Equal(CmaConnectionStatus.Connected, h.Store.Get(Company).Status);
    }

    [Fact]
    public async Task Session_LoginFails_SetsErrorAndRetries()
    {
        await using var h = new Harness();
        await h.SeedAsync(true, Instrument("ZSF7", 30));
        h.Client.LoginFailures.Enqueue(new HttpRequestException("https://feed.exemplo.com/execute?JSONRequest=" + Password, null, HttpStatusCode.ServiceUnavailable));
        h.Client.LoginFailures.Enqueue(new CmaAuthenticationException());
        h.Start();

        await WaitUntil(() => h.Store.Get(Company).Status == CmaConnectionStatus.Connected, "conectado");

        Assert.Equal(3, h.Client.LoginCalls);
        var errors = h.Logger.Entries.Where(e => e.Level == Microsoft.Extensions.Logging.LogLevel.Warning).Select(e => e.Message).ToList();
        Assert.Equal(2, errors.Count);
        Assert.Contains("CMA indisponível: HTTP 503", errors[0]);
        Assert.Contains("Falha de autenticação na CMA", errors[1]);
        Assert.All(h.Logger.Entries, e =>
        {
            Assert.DoesNotContain(Password, e.Message);
            Assert.DoesNotContain("execute", e.Message);
        });
    }

    [Fact]
    public async Task Session_ErrorStatusMessage_HasNoSecrets()
    {
        await using var h = new Harness();
        await h.SeedAsync(true, Instrument("ZSF7", 30));
        h.Client.LoginFailures.Enqueue(new HttpRequestException("https://feed.exemplo.com/execute?JSONRequest=" + Password));
        h.Start(backoff: TimeSpan.FromHours(1)); // fica parado no Error até o teste acabar

        await WaitUntil(() => h.Store.Get(Company).Status == CmaConnectionStatus.Error, "erro");
        var message = h.Store.Get(Company).Message!;
        Assert.Equal("CMA indisponível: HttpRequestException", message);
    }

    [Fact]
    public async Task Session_SubscribesOnlyActiveInstrumentsWithSourceId()
    {
        await using var h = new Harness();
        await h.SeedAsync(true, Instrument("ZSF7", 30), Instrument("CCMF27", 57), Instrument("SEMFONTE", null), Instrument("INATIVO", 30, active: false));
        h.Start();

        await WaitUntil(() => h.Client.SubscriptionCount == 1, "assinatura");
        Assert.Equal(new[] { ((int?)30, "ZSF7"), ((int?)57, "CCMF27") }.OrderBy(s => s.Item2),
            h.Client.Subscriptions[0].OrderBy(s => s.Symbol));
    }

    [Fact]
    public async Task Session_InstrumentListChanged_Resubscribes()
    {
        await using var h = new Harness();
        await h.SeedAsync(true, Instrument("ZSF7", 30));
        h.Start(reload: TimeSpan.Zero);

        await WaitUntil(() => h.Client.SubscriptionCount == 1, "1ª assinatura");
        await using (var context = h.NewContext())
        {
            context.MarketInstruments.Add(Instrument("ZCH7", 30));
            await context.SaveChangesAsync();
        }

        await WaitUntil(() => h.Client.SubscriptionCount == 2, "reassinatura");
        Assert.Equal(2, h.Client.Subscriptions[1].Count);
        Assert.Equal(1, h.Client.LoginCalls);
    }

    [Fact]
    public async Task Session_ReconnectRequest_LogsInAgain()
    {
        await using var h = new Harness();
        await h.SeedAsync(true, Instrument("ZSF7", 30));
        h.Start();
        await WaitUntil(() => h.Store.Get(Company).Status == CmaConnectionStatus.Connected, "conectado");

        h.Store.RequestReconnect(Company);

        await WaitUntil(() => h.Client.LoginCalls == 2 && h.Client.SubscriptionCount == 2, "novo login");
    }

    [Fact]
    public async Task Session_DisabledSettings_SetsDisabledAndDoesNotLogin()
    {
        await using var h = new Harness();
        await h.SeedAsync(false, Instrument("ZSF7", 30));
        h.Store.SetStatus(Company, CmaConnectionStatus.Error, "anterior");
        h.Start();

        await WaitUntil(() => h.Store.Get(Company).Status == CmaConnectionStatus.Disabled, "desligado");
        Assert.Equal(0, h.Client.LoginCalls);
    }

    private static async Task AssertReconnectsAfterChannelLoss(Harness h, Action loseChannel, string expectedError)
    {
        await WaitUntil(() => h.Store.Get(Company).Status == CmaConnectionStatus.Connected, "conectado");
        loseChannel();
        await WaitUntil(() => h.Client.LoginCalls == 2 && h.Store.Get(Company).Status == CmaConnectionStatus.Connected, "novo login");
        Assert.Contains(h.Logger.Entries, e => e.Level == Microsoft.Extensions.Logging.LogLevel.Warning && e.Message.Contains(expectedError));
    }

    [Fact]
    public async Task Session_ChannelEof_SetsErrorAndLogsInAgain()
    {
        await using var h = new Harness();
        await h.SeedAsync(true, Instrument("ZSF7", 30));
        h.Start();
        await AssertReconnectsAfterChannelLoss(h, () => h.Client.CloseChannel(), "CMA fechou o canal de cotações");
    }

    [Fact]
    public async Task Session_ChannelErrorResponse_SetsErrorAndLogsInAgain()
    {
        await using var h = new Harness();
        await h.SeedAsync(true, Instrument("ZSF7", 30));
        h.Start();
        await AssertReconnectsAfterChannelLoss(h,
            () => h.Client.CloseChannel(new CmaProtocolException("CMA encerrou a sessão (ErrorResponse no canal)")), "ErrorResponse");
    }

    [Fact]
    public async Task Session_IdleChannel_SetsErrorAndLogsInAgain()
    {
        await using var h = new Harness();
        await h.SeedAsync(true, Instrument("ZSF7", 30));
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 11, 5, 10, 0, 0, TimeSpan.Zero));
        h.Start(time: time);
        await AssertReconnectsAfterChannelLoss(h, () => time.Advance(TimeSpan.FromSeconds(91)), "CMA sem atividade no canal");
    }

    [Fact]
    public async Task Session_HeartbeatKeepsChannelAlive()
    {
        await using var h = new Harness();
        await h.SeedAsync(true, Instrument("ZSF7", 30));
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 11, 5, 10, 0, 0, TimeSpan.Zero));
        h.Start(time: time);
        await WaitUntil(() => h.Store.Get(Company).Status == CmaConnectionStatus.Connected, "conectado");

        // 4 × 60 s = 240 s, bem acima do limite de 90 s — só o heartbeat a cada 60 s mantém a conexão.
        for (var i = 1; i <= 4; i++)
        {
            time.Advance(TimeSpan.FromSeconds(60));
            h.Client.Deliver("""{"name":"HeartBeat"}""");
            var expected = i;
            await WaitUntil(() => h.Client.ProcessedMessages == expected, "heartbeat lido");
        }

        // Dois flushes seguidos garantem ao menos um tick inteiro (com a checagem de inatividade) depois do último avanço.
        for (var i = 0; i < 2; i++)
        {
            var before = h.Store.Get(Company).Version;
            h.Client.Deliver(Quote("ZSF7", $"{1000 + i}"));
            await WaitUntil(() => h.Store.Get(Company).Version > before, "tick após o heartbeat");
        }

        Assert.Equal(1, h.Client.LoginCalls);
        Assert.Equal(CmaConnectionStatus.Connected, h.Store.Get(Company).Status);
    }

    [Fact]
    public void Framer_OversizedMessage_Throws()
    {
        var framer = new CmaChannelFramer();
        framer.Push("{\"a\":\"");
        var chunk = new string('x', 64 * 1024);
        Assert.Throws<CmaProtocolException>(() =>
        {
            for (var i = 0; i < 20; i++)
                framer.Push(chunk);
        });
    }

    [Fact]
    public void Describe_CommandTimeout_IsReadable()
    {
        Assert.Equal("CMA não respondeu (tempo esgotado)", CmaCompanySession.Describe(new TaskCanceledException()));
        Assert.Equal("CMA indisponível: HTTP 502",
            CmaCompanySession.Describe(new HttpRequestException("https://h/execute?JSONRequest=x", null, HttpStatusCode.BadGateway)));
    }

    [Fact]
    public void CmaQuoteBatch_MergesDeltas_AndRestoreKeepsNewer()
    {
        var batch = new CmaQuoteBatch();
        batch.Add(MarketTestHelpers.Update("ZSF7", 10m) with { Bid = 9m });
        batch.Add(MarketTestHelpers.Update("ZSF7", null) with { Ask = 11m });
        var taken = batch.TakeAll().Single();
        Assert.Equal((10m, 9m, 11m), (taken.Last!.Value, taken.Bid!.Value, taken.Ask!.Value));

        batch.Add(MarketTestHelpers.Update("ZSF7", 12m));
        batch.Restore([taken]);
        var merged = batch.TakeAll().Single();
        Assert.Equal(12m, merged.Last);
        Assert.Equal(9m, merged.Bid);
    }

    [Fact]
    public async Task CmaStreamerClient_BuildsExecuteUrl_AndParsesLogin()
    {
        var handler = new StubHttpMessageHandler()
            .EnqueueResponse(HttpStatusCode.OK, """{"name":"LoginResponse","success":true,"sessionId":"abc123"}""")
            .EnqueueResponse(HttpStatusCode.OK, """{"name":"QuotesResponse","success":true,"arrQuotes":[{"symbolId":{"sourceId":"30","symbol":"ZSF7"},"arrValues":[{"10":"1"}]}]}""")
            .EnqueueResponse(HttpStatusCode.OK,
                "{\"name\":\"HeartBeat\"}\r\n" + Quote("ZSF7", "1188.25") + "\n" + Quote("ZCH7", "4{5}0") + "{\"name\":\"ArrivalResponse\"}");
        var client = new CmaStreamerClient(new HttpClient(handler));
        var credentials = new CmaCredentials("https://feed.exemplo.com:443/", "usuario", "s&nha =1");

        var session = await client.LoginAsync(credentials, CancellationToken.None);
        var snapshot = await client.SubscribeAsync(credentials, session, [(30, "ZSF7")], CancellationToken.None);
        var channel = new List<JsonElement>();
        await foreach (var q in client.OpenChannelAsync(credentials, session, CancellationToken.None))
            channel.Add(q);

        Assert.Equal("abc123", session);
        var login = handler.Requests[0].RequestUri!;
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
        Assert.Equal("https://feed.exemplo.com/execute", login.GetLeftPart(UriPartial.Path));
        Assert.StartsWith("?JSONRequest=", login.Query);
        Assert.DoesNotContain(" ", login.AbsoluteUri);
        var loginJson = JsonNode.Parse(Uri.UnescapeDataString(login.Query["?JSONRequest=".Length..]))!;
        Assert.Equal("LoginRequest", (string?)loginJson["name"]);
        Assert.Equal("s", (string?)loginJson["type"]);
        Assert.Equal("m", (string?)loginJson["service"]);
        Assert.Equal("usuario", (string?)loginJson["user"]);
        Assert.Equal("s&nha =1", (string?)loginJson["pass"]);
        Assert.Equal("", (string?)loginJson["sessionId"]);
        Assert.False((bool)loginJson["mdtp"]!["OMSIsOperator"]!);

        var quotesJson = JsonNode.Parse(Uri.UnescapeDataString(handler.Requests[1].RequestUri!.Query["?JSONRequest=".Length..]))!;
        Assert.Equal("QuotesRequest", (string?)quotesJson["name"]);
        Assert.Equal("n", (string?)quotesJson["type"]);
        Assert.Equal("abc123", (string?)quotesJson["sessionId"]);
        Assert.True((bool)quotesJson["realtime"]!);
        Assert.True((bool)quotesJson["sync"]!);
        Assert.Equal("30", (string?)quotesJson["symbols"]![0]!["sourceId"]);
        Assert.Equal("ZSF7", (string?)quotesJson["symbols"]![0]!["symbol"]);
        Assert.Single(snapshot);

        var channelJson = JsonNode.Parse(Uri.UnescapeDataString(handler.Requests[2].RequestUri!.Query["?JSONRequest=".Length..]))!;
        Assert.Equal("CreatePersistentChannelRequest", (string?)channelJson["name"]);
        Assert.False((bool)channelJson["sync"]!);
        // Toda mensagem sai do canal (sinal de vida); só as QuotesUpdateResponse são cotação.
        Assert.Equal(new[] { "HeartBeat", "QuotesUpdateResponse", "QuotesUpdateResponse", "ArrivalResponse" },
            channel.Select(m => m.GetProperty("name").GetString()));
        Assert.Equal(2, channel.Count(CmaStreamerClient.IsQuoteUpdate));
        Assert.Equal("ZCH7", channel[2].GetProperty("symbolId").GetProperty("symbol").GetString());
    }

    private static Task Ping(HttpStatusCode status, string body) =>
        new CmaStreamerClient(new HttpClient(new StubHttpMessageHandler(status, body)))
            .PingAsync(new CmaCredentials("https://h", "u", Password), "s", CancellationToken.None);

    [Fact]
    public Task Ping_EmptyBody_IsTolerated() => Ping(HttpStatusCode.OK, "");

    [Fact]
    public async Task Ping_NonJsonBody_IsTolerated()
    {
        await Ping(HttpStatusCode.OK, "OK");
        await Ping(HttpStatusCode.OK, """{"name":"PingResponse"}""");
    }

    [Fact]
    public async Task Ping_ErrorResponse_Throws()
    {
        await Assert.ThrowsAsync<CmaProtocolException>(() => Ping(HttpStatusCode.OK, """{"name":"ErrorResponse"}"""));
        await Assert.ThrowsAsync<CmaProtocolException>(() => Ping(HttpStatusCode.OK, """{"name":"PingResponse","success":false}"""));
    }

    [Fact]
    public async Task Ping_HttpError_Throws()
    {
        var e = await Assert.ThrowsAsync<HttpRequestException>(() => Ping(HttpStatusCode.ServiceUnavailable, ""));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, e.StatusCode);
    }

    [Fact]
    public async Task CmaStreamerClient_LoginRejected_ThrowsAuthenticationWithoutSecrets()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, """{"name":"LoginResponse","success":false,"textual":"x"}""");
        var client = new CmaStreamerClient(new HttpClient(handler));

        var e = await Assert.ThrowsAsync<CmaAuthenticationException>(() =>
            client.LoginAsync(new CmaCredentials("https://h", "u", Password), CancellationToken.None));
        Assert.DoesNotContain(Password, e.Message);

        var failing = new CmaStreamerClient(new HttpClient(new StubHttpMessageHandler(HttpStatusCode.ServiceUnavailable)));
        var http = await Assert.ThrowsAsync<HttpRequestException>(() =>
            failing.LoginAsync(new CmaCredentials("https://h", "u", Password), CancellationToken.None));
        Assert.Equal("HTTP 503", http.Message);
    }
}
