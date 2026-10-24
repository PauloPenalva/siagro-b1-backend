using System.Net;
using System.Security.Cryptography.X509Certificates;
using SiagroB1.Domain.Enums;
using SiagroB1.Fiscal.Reinf;

namespace SiagroB1.Fiscal.Tests.Reinf;

public class ReinfHttpClientTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? Request;
        public string? Body;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return respond(request);
        }
    }

    private static string Sample(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Reinf", "Samples", name));

    // Ignorado quando ha handlerOverride.
    private static X509Certificate2 Cert() => null!;

    [Fact]
    public async Task Send_posts_to_restricted_endpoint()
    {
        var stub = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Sample("lote-processando.xml")) });
        var client = new ReinfHttpClient(stub);
        var r = await client.SendAsync("<Reinf/>", Cert(), ReinfEnvironment.ProductionRestricted, CancellationToken.None);
        Assert.Equal(HttpMethod.Post, stub.Request!.Method);
        Assert.Equal("https://pre-reinf.receita.economia.gov.br/recepcao/lotes", stub.Request.RequestUri!.ToString());
        Assert.Equal("<Reinf/>", stub.Body);
        Assert.Equal("2.202610.1234567", r.Protocol);
    }

    [Fact]
    public async Task Query_gets_production_endpoint()
    {
        var stub = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Sample("lote-processado.xml")) });
        var client = new ReinfHttpClient(stub);
        var r = await client.QueryAsync("2.202610.1234567", Cert(), ReinfEnvironment.Production, CancellationToken.None);
        Assert.Equal(HttpMethod.Get, stub.Request!.Method);
        Assert.Equal("https://reinf.receita.economia.gov.br/consulta/lotes/2.202610.1234567", stub.Request.RequestUri!.ToString());
        Assert.Equal(2, r.Events.Count);
    }

    [Fact]
    public async Task Http_error_throws_transport_exception()
    {
        var stub = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("boom") });
        var client = new ReinfHttpClient(stub);
        var ex = await Assert.ThrowsAsync<ReinfTransportException>(() =>
            client.SendAsync("<Reinf/>", Cert(), ReinfEnvironment.ProductionRestricted, CancellationToken.None));
        Assert.Equal(500, ex.StatusCode);
        Assert.Equal("boom", ex.Body);
    }

    [Fact]
    public async Task Network_failure_wraps_with_status_zero()
    {
        var stub = new StubHandler(_ => throw new HttpRequestException("down"));
        var client = new ReinfHttpClient(stub);
        var ex = await Assert.ThrowsAsync<ReinfTransportException>(() =>
            client.QueryAsync("1", Cert(), ReinfEnvironment.ProductionRestricted, CancellationToken.None));
        Assert.Equal(0, ex.StatusCode);
    }

    [Fact]
    public async Task Unparseable_2xx_body_is_unknown_outcome()
    {
        var stub = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>oops") });
        var client = new ReinfHttpClient(stub);
        await Assert.ThrowsAsync<ReinfUnknownOutcomeException>(() =>
            client.SendAsync("<Reinf/>", Cert(), ReinfEnvironment.ProductionRestricted, CancellationToken.None));
    }

    [Fact]
    public async Task Timeout_is_unknown_outcome()
    {
        var stub = new StubHandler(_ => throw new TaskCanceledException("timeout"));
        var client = new ReinfHttpClient(stub);
        await Assert.ThrowsAsync<ReinfUnknownOutcomeException>(() =>
            client.SendAsync("<Reinf/>", Cert(), ReinfEnvironment.ProductionRestricted, CancellationToken.None));
    }
}
