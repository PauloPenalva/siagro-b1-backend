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

    public HttpRequestMessage? LastRequest { get; private set; }
    public string? LastRequestBody { get; private set; }
    public int CallCount { get; private set; }

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

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        CallCount++;
        LastRequest = request;

        if (request.Content is not null)
            LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);

        if (_throwOnSend is not null)
            throw _throwOnSend;

        return new HttpResponseMessage(_statusCode)
        {
            Content = new StringContent(_responseBody, Encoding.UTF8, "application/json"),
        };
    }
}
