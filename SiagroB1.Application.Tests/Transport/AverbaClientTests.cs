using System.Net;
using Microsoft.Extensions.Configuration;
using SiagroB1.Application.Services.Transport;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.Transport;

public class AverbaClientTests
{
    private static IConfiguration Config(string? production = "http://prod.averba", string? homologation = "http://hom.averba") =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Averba:ProductionUrl"] = production,
            ["Averba:HomologationUrl"] = homologation,
        }).Build();

    private static AverbaClient Client(StubHttpMessageHandler handler, IConfiguration? config = null) =>
        new(new HttpClient(handler), config ?? Config());

    [Fact]
    public async Task Login_success_reads_bearer()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, "{\"Bearer\":\"tok123\"}");

        var result = await Client(handler).LoginAsync(NfeEnvironment.Production, "COSTA", "s3nha", "11371106", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("tok123", result.Bearer);
        Assert.Equal("http://prod.averba/rest/Auth", handler.LastRequest!.RequestUri!.ToString());
        Assert.Contains("\"usuario\":\"COSTA\"", handler.LastRequestBody);
        Assert.Contains("\"senha\":\"s3nha\"", handler.LastRequestBody);
        Assert.Contains("\"codigoatm\":\"11371106\"", handler.LastRequestBody);
    }

    [Fact]
    public async Task Login_sends_accept_json_header()
    {
        // Sem Accept a AT&M recusa com "Header Accept nao informado" (visto no teste real).
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, "{\"Bearer\":\"t\"}");

        await Client(handler).LoginAsync(NfeEnvironment.Production, "x", "y", "z", CancellationToken.None);

        Assert.Contains(handler.LastRequest!.Headers.Accept, h => h.MediaType == "application/json");
    }

    [Fact]
    public async Task Login_sends_plain_json_content_type_without_charset()
    {
        // A AT&M recusa "application/json; charset=utf-8": "Header Content-Type invalido" (visto no teste real).
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, "{\"Bearer\":\"t\"}");

        await Client(handler).LoginAsync(NfeEnvironment.Production, "x", "y", "z", CancellationToken.None);

        Assert.Equal("application/json", handler.LastRequest!.Content!.Headers.ContentType!.ToString());
    }

    [Fact]
    public async Task Login_error_reads_description()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK,
            "{\"Erros\":{\"Erro\":[{\"Codigo\":\"901\",\"Descricao\":\"Usuário ou senha inválidos\"}]}}");

        var result = await Client(handler).LoginAsync(NfeEnvironment.Production, "x", "y", "z", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("Usuário ou senha inválidos", result.Error);
    }

    [Fact]
    public async Task Login_posts_to_homologation_url_when_env_is_homologation()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, "{\"Bearer\":\"t\"}");

        await Client(handler).LoginAsync(NfeEnvironment.Homologation, "x", "y", "z", CancellationToken.None);

        Assert.Equal("http://hom.averba/rest/Auth", handler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task Missing_url_is_reported()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, "{}");

        var result = await Client(handler, Config(production: null)).LoginAsync(NfeEnvironment.Production, "x", "y", "z", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("Configure Averba:ProductionUrl/HomologationUrl no servidor.", result.Error);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task Http_error_status_is_reported()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.InternalServerError, "falhou");

        var result = await Client(handler).LoginAsync(NfeEnvironment.Production, "x", "y", "z", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("A AT&M respondeu HTTP 500.", result.Error);
    }

    [Fact]
    public async Task Http_error_with_error_body_reads_description()
    {
        // A AT&M recusa credencial inválida com HTTP 400 e o motivo no corpo.
        var handler = new StubHttpMessageHandler(HttpStatusCode.BadRequest,
            "{\"Erros\":{\"Erro\":[{\"Codigo\":\"901\",\"Descricao\":\"Usuário não encontrado\"}]}}");

        var result = await Client(handler).LoginAsync(NfeEnvironment.Production, "x", "y", "z", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("Usuário não encontrado", result.Error);
    }

    [Fact]
    public async Task Network_failure_is_reported()
    {
        var handler = new StubHttpMessageHandler(new HttpRequestException("sem rota"));

        var result = await Client(handler).LoginAsync(NfeEnvironment.Production, "x", "y", "z", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("Não foi possível conectar à AT&M: sem rota", result.Error);
    }

    private const string TwoInsurances =
        "{\"Numero\":\"10\",\"Averbado\":{\"dhAverbacao\":\"2026-10-22T10:00:00\",\"Protocolo\":\"PROT1\",\"DadosSeguro\":["
        + "{\"NumeroAverbacao\":\"A1\",\"CNPJSeguradora\":\"111\",\"NomeSeguradora\":\"Seg 1\",\"NumApolice\":\"P1\",\"TpMov\":\"1\",\"ValorAverbado\":\"100.50\",\"RamoAverbado\":\"R1\"},"
        + "{\"NumeroAverbacao\":\"A2\",\"CNPJSeguradora\":\"222\",\"NomeSeguradora\":\"Seg 2\",\"NumApolice\":\"P2\",\"TpMov\":\"1\",\"ValorAverbado\":\"200\",\"RamoAverbado\":\"R2\"}]}}";

    [Fact]
    public async Task SendCte_posts_xml_with_bearer_and_exact_content_type()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, TwoInsurances);

        await Client(handler).SendCteAsync(NfeEnvironment.Production, "tok", "<cteProc/>", CancellationToken.None);

        var request = handler.LastRequest!;
        Assert.Equal("http://prod.averba/rest/CTE", request.RequestUri!.ToString());
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("tok", request.Headers.Authorization.Parameter);
        Assert.Equal("application/xml", request.Content!.Headers.ContentType!.ToString());
        Assert.Contains(request.Headers.Accept, h => h.MediaType == "application/json");
        Assert.Equal("<cteProc/>", handler.LastRequestBody);
    }

    [Fact]
    public async Task SendCte_success_reads_protocol_and_all_insurances()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, TwoInsurances);

        var result = await Client(handler).SendCteAsync(NfeEnvironment.Production, "tok", "<x/>", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("PROT1", result.Protocol);
        Assert.Equal("2026-10-22T10:00:00", result.AverbadoAt);
        Assert.Equal(2, result.Insurances.Count);
        var first = result.Insurances[0];
        Assert.Equal("A1", first.EndorsementNumber);
        Assert.Equal("111", first.InsurerTaxId);
        Assert.Equal("Seg 1", first.InsurerName);
        Assert.Equal("P1", first.PolicyNumber);
        Assert.Equal("100.50", first.Amount);
        Assert.Equal("R1", first.Branch);
        Assert.Equal("1", first.MovementType);
        Assert.Equal("A2", result.Insurances[1].EndorsementNumber);
    }

    [Fact]
    public async Task SendCte_errors_list_is_joined()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK,
            "{\"Erros\":[{\"Codigo\":\"1\",\"Descricao\":\"Erro um\"},{\"Codigo\":\"2\",\"Descricao\":\"Erro dois\"}]}");

        var result = await Client(handler).SendCteAsync(NfeEnvironment.Production, "tok", "<x/>", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("Erro um; Erro dois", result.Error);
    }

    [Fact]
    public async Task SendCte_http_400_with_errors_reads_description()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.BadRequest,
            "{\"Erros\":[{\"Codigo\":\"1\",\"Descricao\":\"CT-e já averbado\"}]}");

        var result = await Client(handler).SendCteAsync(NfeEnvironment.Production, "tok", "<x/>", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("CT-e já averbado", result.Error);
    }

    [Fact]
    public async Task SendCte_network_failure_is_reported()
    {
        var handler = new StubHttpMessageHandler(new HttpRequestException("sem rota"));

        var result = await Client(handler).SendCteAsync(NfeEnvironment.Production, "tok", "<x/>", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("Não foi possível conectar à AT&M: sem rota", result.Error);
    }
}
