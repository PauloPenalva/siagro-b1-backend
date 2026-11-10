using System.Net;
using SiagroB1.Installer.Core.Client;

namespace SiagroB1.Installer.Tests.Client;

public class ClientInstallTests
{
    private static ClientInstallSettings Valid(string codes = "TS01", string url = "ws://servidor:55000/ws/truck-scale",
        string key = "abc", string dir = @"C:\Temp\Client") => new(codes, url, key, dir);

    [Fact]
    public void Valid_settings_have_no_errors() => Assert.Empty(Valid().Validate());

    [Theory]
    [InlineData("http://servidor/ws/truck-scale")]
    [InlineData("ws://servidor:55000/ws/truck-scale/")]
    [InlineData("ws://servidor:55000/ws/truck-scale?x=1")]
    [InlineData("servidor")]
    public void Invalid_url_is_rejected(string url) => Assert.Single(Valid(url: url).Validate());

    [Fact]
    public void Empty_key_and_codes_are_rejected() =>
        Assert.Equal(2, Valid(codes: " , ", key: " ").Validate().Count);

    [Fact]
    public void Relative_directory_is_rejected() => Assert.Single(Valid(dir: "Client").Validate());

    [Fact]
    public void Codes_are_split_trimmed_and_deduplicated() =>
        Assert.Equal(["TS01", "TS02"], Valid(codes: "TS01, ts01;TS02\nTS02").Codes);

    [Fact]
    public void AppSettings_never_enable_mock_and_roundtrip()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var settings = Valid(codes: "TS01, TS02", dir: dir);
            settings.Write();

            var json = File.ReadAllText(Path.Combine(dir, "appsettings.json"));
            Assert.Contains("\"UseMockScale\": false", json);

            var back = ClientInstallSettings.FromInstalled(dir)!;
            Assert.Equal(["TS01", "TS02"], back.Codes);
            Assert.Equal(settings.WebSocketUrl, back.WebSocketUrl);
            Assert.Equal("abc", back.ClientKey);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void FromInstalled_returns_null_without_file() =>
        Assert.Null(ClientInstallSettings.FromInstalled(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));

    [Theory]
    [InlineData(400, true)]
    [InlineData(401, false)]
    [InlineData(404, false)]
    [InlineData(500, false)]
    public void Status_codes_are_interpreted(int status, bool ok) =>
        Assert.Equal(ok, ClientConnectionProbe.Interpret(status).Ok);

    [Fact]
    public async Task Probe_sends_http_url_with_scale_and_key()
    {
        var handler = new StubHandler(HttpStatusCode.BadRequest);
        var result = await ClientConnectionProbe.ProbeAsync("ws://srv:5246/ws/truck-scale", "TS01", "k", handler);

        Assert.True(result.Ok);
        Assert.Equal("http://srv:5246/ws/truck-scale?truckScaleId=TS01", handler.Request!.RequestUri!.ToString());
        Assert.Equal("k", handler.Request.Headers.GetValues("X-Scale-Client-Key").Single());
    }

    [Fact]
    public async Task Probe_reports_unreachable_server()
    {
        var result = await ClientConnectionProbe.ProbeAsync("ws://srv/ws", "TS01", "k", new StubHandler(throws: true));
        Assert.False(result.Ok);
    }

    private sealed class StubHandler(HttpStatusCode status = HttpStatusCode.OK, bool throws = false) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            return throws
                ? throw new HttpRequestException("down")
                : Task.FromResult(new HttpResponseMessage(status));
        }
    }
}
