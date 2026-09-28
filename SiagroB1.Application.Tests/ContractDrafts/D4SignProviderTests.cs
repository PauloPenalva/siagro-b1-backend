using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra.ESignature.D4Sign;

namespace SiagroB1.Application.Tests.ContractDrafts;

public class D4SignProviderTests
{
    private const string Token = "TOKEN-SECRETO";
    private const string Crypt = "CRYPT-SECRETO";

    private static (D4SignProvider Provider, StubHttpMessageHandler Handler) Build(StubHttpMessageHandler handler)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [D4SignProvider.TokenApiKey] = Token,
            [D4SignProvider.CryptKeyKey] = Crypt,
            [D4SignProvider.SafeIdKey] = "COFRE-1",
        }).Build();

        var client = new HttpClient(handler) { BaseAddress = new Uri("https://sandbox.d4sign.com.br/api/v1/") };
        return (new D4SignProvider(client, configuration, NullLogger<D4SignProvider>.Instance), handler);
    }

    private static ESignatureSendRequest Request() => new(
        "Contrato PC-1", "PC-1.pdf", [0x25, 0x50, 0x44, 0x46],
        [new ESignatureSigner("Ana", "ana@x.com", SignatoryRole.SignAsParty, 1),
         new ESignatureSigner("Beto", "beto@x.com", SignatoryRole.SignAsWitness, 2)],
        "https://gw.example.com/hooks/d4sign/SEGREDO", "Assine, por favor");

    [Fact]
    public async Task Send_walks_upload_createlist_webhook_and_sendtosigner_in_order()
    {
        var handler = new StubHttpMessageHandler()
            .EnqueueResponse(HttpStatusCode.OK, "{\"uuid\":\"UUID-1\"}")
            .EnqueueResponse(HttpStatusCode.OK, "{\"message\":\"ok\"}")
            .EnqueueResponse(HttpStatusCode.OK, "{\"message\":\"ok\"}")
            .EnqueueResponse(HttpStatusCode.OK, "{\"message\":\"enviado\"}");
        var (provider, _) = Build(handler);

        var result = await provider.SendAsync(Request());

        Assert.True(result.Succeeded);
        Assert.Equal("UUID-1", result.ExternalDocumentId);
        Assert.Equal(4, handler.Requests.Count);
        Assert.Contains("/documents/COFRE-1/upload", handler.Requests[0].RequestUri!.ToString());
        Assert.Contains("/documents/UUID-1/createlist", handler.Requests[1].RequestUri!.ToString());
        Assert.Contains("/documents/UUID-1/webhooks", handler.Requests[2].RequestUri!.ToString());
        Assert.Contains("/documents/UUID-1/sendtosigner", handler.Requests[3].RequestUri!.ToString());
    }

    [Fact]
    public async Task Upload_carries_credentials_in_headers_and_the_rest_in_the_query()
    {
        var handler = new StubHttpMessageHandler()
            .EnqueueResponse(HttpStatusCode.OK, "{\"uuid\":\"UUID-1\"}")
            .EnqueueResponse(HttpStatusCode.OK).EnqueueResponse(HttpStatusCode.OK).EnqueueResponse(HttpStatusCode.OK);
        var (provider, _) = Build(handler);

        await provider.SendAsync(Request());

        Assert.True(handler.Requests[0].Headers.Contains("tokenAPI"));
        Assert.True(handler.Requests[0].Headers.Contains("cryptKey"));
        Assert.DoesNotContain(Token, handler.Requests[0].RequestUri!.Query);
        Assert.Contains($"tokenAPI={Token}", handler.Requests[1].RequestUri!.Query);
        Assert.Contains($"cryptKey={Crypt}", handler.Requests[1].RequestUri!.Query);
    }

    [Fact]
    public async Task Createlist_maps_the_role_to_the_d4sign_act()
    {
        var handler = new StubHttpMessageHandler()
            .EnqueueResponse(HttpStatusCode.OK, "{\"uuid\":\"UUID-1\"}")
            .EnqueueResponse(HttpStatusCode.OK).EnqueueResponse(HttpStatusCode.OK).EnqueueResponse(HttpStatusCode.OK);
        var (provider, _) = Build(handler);

        await provider.SendAsync(Request());

        var body = handler.RequestBodies[1]!;
        Assert.Contains("\"email\":\"ana@x.com\"", body);
        Assert.Contains("\"act\":\"4\"", body);   // SignAsParty
        Assert.Contains("\"act\":\"5\"", body);   // SignAsWitness
        Assert.Contains("\"embed_methodauth\":\"email\"", body);
    }

    [Fact]
    public async Task A_failure_after_the_upload_cancels_the_orphan_document()
    {
        var handler = new StubHttpMessageHandler()
            .EnqueueResponse(HttpStatusCode.OK, "{\"uuid\":\"UUID-1\"}")
            .EnqueueResponse(HttpStatusCode.BadRequest, "{\"message\":\"signatario invalido\"}")
            .EnqueueResponse(HttpStatusCode.OK, "{\"message\":\"cancelado\"}");
        var (provider, _) = Build(handler);

        var result = await provider.SendAsync(Request());

        Assert.False(result.Succeeded);
        Assert.False(result.Transient);
        Assert.Contains("/documents/UUID-1/cancel", handler.Requests[2].RequestUri!.ToString());
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout, true)]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    [InlineData(HttpStatusCode.BadGateway, true)]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    public async Task Transient_is_decided_by_the_status_code(HttpStatusCode status, bool transient)
    {
        var handler = new StubHttpMessageHandler().EnqueueResponse(status, "{\"message\":\"x\"}");
        var (provider, _) = Build(handler);

        var result = await provider.SendAsync(Request());

        Assert.False(result.Succeeded);
        Assert.Equal(transient, result.Transient);
    }

    [Fact]
    public async Task Network_failure_is_transient_and_never_leaks_the_secret()
    {
        var handler = new StubHttpMessageHandler(new HttpRequestException($"falha ao chamar https://x?tokenAPI={Token}"));
        var (provider, _) = Build(handler);

        var result = await provider.SendAsync(Request());

        Assert.True(result.Transient);
        Assert.DoesNotContain(Token, result.ErrorMessage);
    }

    [Fact]
    public async Task Provider_error_message_is_truncated_at_300_characters()
    {
        var handler = new StubHttpMessageHandler().EnqueueResponse(HttpStatusCode.BadRequest, new string('x', 900));
        var (provider, _) = Build(handler);

        var result = await provider.SendAsync(Request());

        Assert.NotNull(result.ErrorMessage);
        Assert.True(result.ErrorMessage!.Length <= 300, $"tinha {result.ErrorMessage.Length}");
    }

    [Fact]
    public async Task Get_state_reads_the_document_status_and_the_signer_list()
    {
        var handler = new StubHttpMessageHandler()
            .EnqueueResponse(HttpStatusCode.OK, "[{\"uuidDoc\":\"UUID-1\",\"statusId\":\"4\"}]")
            .EnqueueResponse(HttpStatusCode.OK,
                "{\"list\":[{\"email\":\"ana@x.com\",\"signed\":\"1\",\"signed_date\":\"2026-09-22 10:00:00\"}," +
                "{\"email\":\"beto@x.com\",\"signed\":\"0\",\"signed_date\":null}]}");
        var (provider, _) = Build(handler);

        var state = await provider.GetStateAsync("UUID-1");

        Assert.Equal(ESignatureDocumentStatus.Finished, state.Status);
        Assert.Equal(2, state.Signers.Count);
        Assert.True(state.Signers.Single(s => s.Email == "ana@x.com").Signed);
        Assert.False(state.Signers.Single(s => s.Email == "beto@x.com").Signed);
    }

    [Fact]
    public async Task Get_state_returns_unknown_when_the_provider_fails()
    {
        var handler = new StubHttpMessageHandler().EnqueueResponse(HttpStatusCode.InternalServerError, "boom");
        var (provider, _) = Build(handler);

        var state = await provider.GetStateAsync("UUID-1");

        Assert.Equal(ESignatureDocumentStatus.Unknown, state.Status);
        Assert.Empty(state.Signers);
        Assert.NotNull(state.ErrorMessage);
    }
}
