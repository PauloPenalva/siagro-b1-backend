using System.Net;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Interfaces;

namespace SiagroB1.Application.Tests.ContractDrafts;

public class FakeESignatureProviderTests
{
    [Fact]
    public async Task Scripted_failure_is_returned_not_thrown()
    {
        var provider = new FakeESignatureProvider().FailsTransiently("503");

        var result = await provider.SendAsync(new ESignatureSendRequest("t", "f.pdf", [1], [], "", "m"));

        Assert.False(result.Succeeded);
        Assert.True(result.Transient);
        Assert.Equal("503", result.ErrorMessage);
        Assert.Equal(1, provider.SendCalls);
    }

    [Fact]
    public async Task Stub_handler_serves_one_response_per_call_in_order()
    {
        var handler = new StubHttpMessageHandler()
            .EnqueueResponse(HttpStatusCode.OK, "{\"uuid\":\"a\"}")
            .EnqueueResponse(HttpStatusCode.BadRequest, "{\"message\":\"erro\"}");
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com/") };

        var first = await client.GetAsync("um");
        var second = await client.GetAsync("dois");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Contains("uuid", await first.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        Assert.Equal(2, handler.Requests.Count);
    }
}
