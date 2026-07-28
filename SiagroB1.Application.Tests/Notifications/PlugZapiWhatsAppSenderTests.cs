using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Infra.WhatsApp;

namespace SiagroB1.Application.Tests.Notifications;

/// <summary>
/// Primeira integração HTTP de saída do sistema. Duas invariantes importam aqui: o sender nunca
/// lança (quem decide retentar é o job, olhando <c>Transient</c>), e o token da instância —
/// que o PlugZapi exige no PATH da URL — nunca vaza para a mensagem de erro, que é gravada no
/// log e exibida na tela de administração.
/// </summary>
public class PlugZapiWhatsAppSenderTests
{
    private const string Token = "TOKEN-SECRETO-123";

    private static (PlugZapiWhatsAppSender Sender, StubHttpMessageHandler Handler) CreateSender(
        StubHttpMessageHandler handler, string? clientToken = "CLIENT-TOKEN-ABC")
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Notifications:WhatsApp:InstanceId"] = "INSTANCIA-1",
                ["Notifications:WhatsApp:Token"] = Token,
                ["Notifications:WhatsApp:ClientToken"] = clientToken,
                ["Notifications:WhatsApp:DelayMessageSeconds"] = "2",
            })
            .Build();

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.plugzapi.com.br/") };

        return (new PlugZapiWhatsAppSender(
            client, configuration, NullLogger<PlugZapiWhatsAppSender>.Instance), handler);
    }

    [Fact]
    public async Task SendTextAsync_Success_ReturnsProviderMessageId()
    {
        var (sender, _) = CreateSender(new StubHttpMessageHandler(
            HttpStatusCode.OK, """{"zaapId":"3999984263738042930","messageId":"D241XXXX732339502B68"}"""));

        var result = await sender.SendTextAsync("5566999998888", "Olá");

        Assert.True(result.Success);
        Assert.Equal(200, result.HttpStatusCode);
        Assert.Equal("D241XXXX732339502B68", result.ProviderMessageId);
        Assert.False(result.Transient);
    }

    [Fact]
    public async Task SendTextAsync_PostsPhoneAndMessageToInstanceRoute()
    {
        var (sender, handler) = CreateSender(new StubHttpMessageHandler(HttpStatusCode.OK));

        await sender.SendTextAsync("5566999998888", "Contrato incluído");

        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal(
            $"https://api.plugzapi.com.br/instances/INSTANCIA-1/token/{Token}/send-text",
            handler.LastRequest.RequestUri!.ToString());

        var body = JsonDocument.Parse(handler.LastRequestBody!).RootElement;
        Assert.Equal("5566999998888", body.GetProperty("phone").GetString());
        Assert.Equal("Contrato incluído", body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task SendTextAsync_SendsClientTokenHeaderWhenConfigured()
    {
        var (sender, handler) = CreateSender(new StubHttpMessageHandler(HttpStatusCode.OK));

        await sender.SendTextAsync("5566999998888", "Olá");

        Assert.True(handler.LastRequest!.Headers.TryGetValues("Client-Token", out var values));
        Assert.Equal("CLIENT-TOKEN-ABC", Assert.Single(values!));
    }

    /// <summary>
    /// A doc pública do PlugZapi não mostra o Client-Token nos exemplos, mas o suporte o
    /// documenta como obrigatório e ele é ativável no painel. Enviar só quando configurado
    /// funciona nos dois cenários.
    /// </summary>
    [Fact]
    public async Task SendTextAsync_OmitsClientTokenHeaderWhenNotConfigured()
    {
        var (sender, handler) = CreateSender(new StubHttpMessageHandler(HttpStatusCode.OK), clientToken: null);

        await sender.SendTextAsync("5566999998888", "Olá");

        Assert.False(handler.LastRequest!.Headers.Contains("Client-Token"));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.MethodNotAllowed)]
    [InlineData(HttpStatusCode.UnsupportedMediaType)]
    public async Task SendTextAsync_ClientError_IsPermanentFailure(HttpStatusCode statusCode)
    {
        var (sender, _) = CreateSender(new StubHttpMessageHandler(statusCode, """{"error":"instance not connected"}"""));

        var result = await sender.SendTextAsync("5566999998888", "Olá");

        Assert.False(result.Success);
        Assert.False(result.Transient);
        Assert.Equal((int)statusCode, result.HttpStatusCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task SendTextAsync_ServerOrThrottleError_IsTransient(HttpStatusCode statusCode)
    {
        var (sender, _) = CreateSender(new StubHttpMessageHandler(statusCode));

        var result = await sender.SendTextAsync("5566999998888", "Olá");

        Assert.False(result.Success);
        Assert.True(result.Transient);
    }

    [Fact]
    public async Task SendTextAsync_NetworkFailure_IsTransientAndDoesNotThrow()
    {
        var (sender, _) = CreateSender(new StubHttpMessageHandler(new HttpRequestException("conexão recusada")));

        var result = await sender.SendTextAsync("5566999998888", "Olá");

        Assert.False(result.Success);
        Assert.True(result.Transient);
    }

    /// <summary>
    /// O teste que protege o segredo: o token vai no path da URL, então qualquer erro que
    /// carregue a URL o entrega para a tela de log, visível a qualquer administrador.
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task SendTextAsync_ErrorMessage_NeverLeaksTokenOrUrl(HttpStatusCode statusCode)
    {
        var (sender, _) = CreateSender(new StubHttpMessageHandler(statusCode, "erro"));

        var result = await sender.SendTextAsync("5566999998888", "Olá");

        Assert.NotNull(result.Error);
        Assert.DoesNotContain(Token, result.Error);
        Assert.DoesNotContain("plugzapi.com.br", result.Error);
    }

    [Fact]
    public async Task SendTextAsync_NetworkFailure_ErrorNeverLeaksToken()
    {
        var (sender, _) = CreateSender(
            new StubHttpMessageHandler(new HttpRequestException($"falha ao chamar https://api.plugzapi.com.br/instances/INSTANCIA-1/token/{Token}/send-text")));

        var result = await sender.SendTextAsync("5566999998888", "Olá");

        Assert.DoesNotContain(Token, result.Error);
    }
}
