using System.Net;
using SiagroB1.Installer.Core.Services;
using SiagroB1.Installer.Core.Settings;

namespace SiagroB1.Installer.Tests.Services;

public class ServiceCommandBuilderTests
{
    [Fact]
    public void BinPath_Format()
    {
        Assert.Equal("\"C:\\X\\SiagroB1.Web.exe\" --environment Production --urls http://*:55100",
            ServiceCommandBuilder.BinPath(@"C:\X\SiagroB1.Web.exe", "Production", 55100));
    }

    [Fact]
    public void CreateArgs_HasAutoStart()
    {
        var args = ServiceCommandBuilder.CreateArgs("svc", "\"x.exe\" --urls http://*:1", "IDX Sistemas | SiagroB1 API");

        Assert.Equal(new[] { "create", "svc", "binPath=", "\"x.exe\" --urls http://*:1", "DisplayName=", "IDX Sistemas | SiagroB1 API", "start=", "auto" }, args);
    }

    [Fact]
    public void FailureArgs_RestartsThreeTimes()
    {
        Assert.Equal(new[] { "failure", "svc", "reset=", "86400", "actions=", "restart/60000/restart/60000/restart/60000" },
            ServiceCommandBuilder.FailureArgs("svc"));
    }
}

public class PortCheckerTests
{
    private static Dictionary<ServiceKind, int> Ports(int g, int w, int r) =>
        new() { [ServiceKind.Gateway] = g, [ServiceKind.Web] = w, [ServiceKind.Reports] = r };

    private static readonly IReadOnlySet<int> None = new HashSet<int>();

    [Fact]
    public void Validate_Ok()
    {
        Assert.Empty(PortChecker.Validate(Ports(55000, 55100, 55200), _ => false, None));
    }

    [Fact]
    public void Validate_DuplicatePorts()
    {
        var errors = PortChecker.Validate(Ports(55000, 55000, 55200), _ => false, None);

        Assert.Contains("As portas do Gateway e do Web não podem ser iguais.", errors);
    }

    [Fact]
    public void Validate_PortInUse()
    {
        var errors = PortChecker.Validate(Ports(55000, 55100, 55200), p => p == 55000, None);

        Assert.Contains("A porta 55000 já está em uso por outro programa.", errors);
    }

    [Fact]
    public void Validate_OwnPortIgnored()
    {
        Assert.Empty(PortChecker.Validate(Ports(55000, 55100, 55200), _ => true, new HashSet<int> { 55000, 55100, 55200 }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(70000)]
    public void Validate_OutOfRange(int port)
    {
        var errors = PortChecker.Validate(Ports(port, 55100, 55200), _ => false, None);

        Assert.Contains($"A porta do Gateway ({port}) deve estar entre 1 e 65535.", errors);
    }
}

public class EndpointProbeTests
{
    private sealed class Handler(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            await Task.Yield();
            return respond();
        }
    }

    private sealed class SlowHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private static readonly Uri Url = new("http://localhost:55000/");

    [Fact]
    public async Task Probe_401_IsResponding()
    {
        var (ok, status) = await EndpointProbe.ProbeAsync(Url, TimeSpan.FromSeconds(1), new Handler(() => new HttpResponseMessage(HttpStatusCode.Unauthorized)));

        Assert.True(ok);
        Assert.Equal(401, status);
    }

    [Fact]
    public async Task Probe_500_NotResponding()
    {
        var (ok, status) = await EndpointProbe.ProbeAsync(Url, TimeSpan.FromSeconds(1), new Handler(() => new HttpResponseMessage(HttpStatusCode.InternalServerError)));

        Assert.False(ok);
        Assert.Equal(500, status);
    }

    [Fact]
    public async Task Probe_Timeout_NotResponding()
    {
        var (ok, status) = await EndpointProbe.ProbeAsync(Url, TimeSpan.FromMilliseconds(100), new SlowHandler());

        Assert.False(ok);
        Assert.Null(status);
    }

    [Fact]
    public async Task Probe_ConnectionRefused_NotResponding()
    {
        var (ok, _) = await EndpointProbe.ProbeAsync(Url, TimeSpan.FromSeconds(1), new Handler(() => throw new HttpRequestException("recusado")));

        Assert.False(ok);
    }
}
