using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SiagroB1.Application.Services.Market.Cma;

/// <summary>
/// Porta do StreamerConnection.java (cma-data-feed): todo comando é um GET em
/// <c>{host}/execute?JSONRequest=&lt;json url-encoded&gt;</c>. O login leva usuário e senha NA QUERY STRING —
/// por isso este cliente nunca registra URL (o HttpClient é registrado com RemoveAllLoggers) e nenhuma
/// exceção lançada aqui carrega a URL.
/// </summary>
public sealed class CmaStreamerClient(HttpClient httpClient) : ICmaStreamerClient
{
    public const string ExecutePath = "/execute";
    public const string JsonRequestParam = "JSONRequest";
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(15);
    private static long _idGenerator;

    public async Task<string> LoginAsync(CmaCredentials credentials, CancellationToken cancellationToken)
    {
        var request = new JsonObject
        {
            ["id"] = NextId(),
            ["sessionId"] = "",
            ["sync"] = true,
            ["service"] = "m",
            ["type"] = "s",
            ["name"] = "LoginRequest",
            ["version"] = 1,
            ["user"] = credentials.UserName,
            ["pass"] = credentials.Password,
            ["mdtp"] = new JsonObject { ["OMSIsOperator"] = false, ["OMSId"] = "" },
        };

        using var response = await SendCommandAsync(credentials.Host, request, cancellationToken);
        var root = response.RootElement;
        if (Name(root) != "LoginResponse" || !Success(root))
            throw new CmaAuthenticationException();

        var sessionId = root.TryGetProperty("sessionId", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
        if (string.IsNullOrEmpty(sessionId))
            throw new CmaAuthenticationException();
        return sessionId;
    }

    public async Task<IReadOnlyList<JsonElement>> SubscribeAsync(CmaCredentials credentials, string sessionId,
        IReadOnlyCollection<(int? SourceId, string Symbol)> symbols, CancellationToken cancellationToken)
    {
        var symbolArray = new JsonArray();
        foreach (var (sourceId, symbol) in symbols)
            // O Java manda o sourceId como texto ("" + symbol.getSource()).
            symbolArray.Add(new JsonObject { ["sourceId"] = sourceId?.ToString(CultureInfo.InvariantCulture), ["symbol"] = symbol });

        var request = new JsonObject
        {
            ["id"] = NextId(),
            ["sessionId"] = sessionId,
            ["sync"] = true,
            ["type"] = "n",
            ["name"] = "QuotesRequest",
            ["timeoutHandler"] = 120,
            ["failActionType"] = "alert",
            ["fields"] = "",
            ["realtime"] = true,
            ["sign"] = true,
            ["symbols"] = symbolArray,
        };

        using var response = await SendCommandAsync(credentials.Host, request, cancellationToken);
        var root = response.RootElement;
        if (Name(root) != "QuotesResponse" || !Success(root))
            throw new CmaProtocolException($"CMA recusou a assinatura de cotações ({Name(root) ?? "sem nome"})");

        return root.TryGetProperty("arrQuotes", out var quotes) && quotes.ValueKind == JsonValueKind.Array
            ? quotes.EnumerateArray().Select(q => q.Clone()).ToList()
            : [];
    }

    public async Task PingAsync(CmaCredentials credentials, string sessionId, CancellationToken cancellationToken)
    {
        // Igual ao Java: o PingRequest vai com sessionId vazio.
        var request = new JsonObject
        {
            ["id"] = NextId(),
            ["sessionId"] = "",
            ["type"] = "c",
            ["name"] = "PingRequest",
            ["sendBack"] = "Health",
            ["sync"] = true,
            ["timeRequest"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
        };

        using var _ = await SendCommandAsync(credentials.Host, request, cancellationToken);
    }

    public async IAsyncEnumerable<JsonElement> OpenChannelAsync(CmaCredentials credentials, string sessionId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var request = new JsonObject
        {
            ["id"] = NextId(),
            ["sessionId"] = sessionId,
            ["type"] = "c",
            ["name"] = "CreatePersistentChannelRequest",
            ["sync"] = false,
            ["timeoutHandler"] = 120,
            ["failActionType"] = "closeconnection",
        };

        using var message = new HttpRequestMessage(HttpMethod.Get, BuildExecuteUri(credentials.Host, request));
        using var response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        EnsureSuccess(response);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        var framer = new CmaChannelFramer();
        var buffer = new char[8192];

        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0)
                yield break; // servidor fechou o canal

            foreach (var text in framer.Push(buffer.AsSpan(0, read)))
            {
                var root = ParseMessage(text);
                switch (Name(root))
                {
                    case "QuotesUpdateResponse":
                        yield return root;
                        break;
                    case "ErrorResponse":
                        throw new CmaProtocolException("CMA encerrou a sessão (ErrorResponse no canal)");
                    // HeartBeat, ArrivalResponse e desconhecidos: nada a fazer.
                }
            }
        }
    }

    /// <summary><c>{host}/execute?JSONRequest=&lt;json&gt;</c>, com o JSON url-encoded (QueryStringEncoder do Netty).</summary>
    public static Uri BuildExecuteUri(string host, JsonObject request) =>
        new($"{host.TrimEnd('/')}{ExecutePath}?{JsonRequestParam}={Uri.EscapeDataString(request.ToJsonString())}");

    private static JsonElement ParseMessage(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            throw new CmaProtocolException("CMA enviou uma mensagem que não é JSON");
        }
    }

    private async Task<JsonDocument> SendCommandAsync(string host, JsonObject request, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CommandTimeout);

        using var message = new HttpRequestMessage(HttpMethod.Get, BuildExecuteUri(host, request));
        using var response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseContentRead, timeout.Token);
        EnsureSuccess(response);

        var body = await response.Content.ReadAsStringAsync(timeout.Token);
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            throw new CmaProtocolException("CMA respondeu um conteúdo que não é JSON");
        }
    }

    // Mensagem só com o status: nunca a URL (que tem a senha no login).
    private static void EnsureSuccess(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"HTTP {(int)response.StatusCode}", null, response.StatusCode);
    }

    private static long NextId() => Interlocked.Increment(ref _idGenerator);

    private static string? Name(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
            ? n.GetString()
            : null;

    private static bool Success(JsonElement root) =>
        root.TryGetProperty("success", out var s) && s.ValueKind == JsonValueKind.True;
}
