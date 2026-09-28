using System.Net;
using System.Text;

namespace SiagroB1.Application.Tests.Support;

/// <summary>
/// Handler de teste para HttpClient. Primeiro do projeto — não havia nenhuma chamada HTTP de
/// saída no solution antes da notificação por WhatsApp.
///
/// Guarda a última requisição para que o teste possa verificar corpo e cabeçalhos, e permite
/// roteirizar a resposta (ou uma exceção, para simular rede fora).
/// </summary>
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _statusCode;
    private readonly string _responseBody;
    private readonly Exception? _throwOnSend;
    private readonly Queue<(HttpStatusCode Status, string Body)> _scripted = new();

    public HttpRequestMessage? LastRequest { get; private set; }
    public string? LastRequestBody { get; private set; }
    public int CallCount { get; private set; }

    /// <summary>Todas as requisições recebidas, na ordem — para conferir uma sequência de chamadas.</summary>
    public List<HttpRequestMessage> Requests { get; } = [];

    /// <summary>Corpo de cada requisição, no mesmo índice de <see cref="Requests"/>.</summary>
    public List<string?> RequestBodies { get; } = [];

    public StubHttpMessageHandler(HttpStatusCode statusCode, string responseBody = "{}")
    {
        _statusCode = statusCode;
        _responseBody = responseBody;
    }

    public StubHttpMessageHandler(Exception throwOnSend)
    {
        _throwOnSend = throwOnSend;
        _statusCode = HttpStatusCode.OK;
        _responseBody = "{}";
    }

    /// <summary>Construtor da fila: cada chamada consome a próxima resposta enfileirada.</summary>
    public StubHttpMessageHandler()
    {
        _statusCode = HttpStatusCode.OK;
        _responseBody = "{}";
    }

    /// <summary>Enfileira a resposta da próxima chamada. Fila vazia ⇒ cai no status/corpo padrão.</summary>
    public StubHttpMessageHandler EnqueueResponse(HttpStatusCode status, string body = "{}")
    {
        _scripted.Enqueue((status, body));
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        CallCount++;
        LastRequest = request;

        if (request.Content is not null)
            LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);

        Requests.Add(request);
        RequestBodies.Add(LastRequestBody);

        if (_throwOnSend is not null) throw _throwOnSend;

        var (status, body) = _scripted.Count > 0 ? _scripted.Dequeue() : (_statusCode, _responseBody);

        return new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }
}
