using SiagroB1.Installer.Core.Detection;
using SiagroB1.Installer.Core.Settings;

namespace SiagroB1.Installer.Tests.Detection;

public class BinPathParserTests
{
    [Fact]
    public void Parse_LegacyBinPath()
    {
        var parsed = BinPathParser.Parse("\"C:\\IdxSistemas\\SiagroB1\\Producao\\Web\\SiagroB1.Web.exe\" --environment Production --urls http://*:55100");

        Assert.NotNull(parsed);
        Assert.Equal(@"C:\IdxSistemas\SiagroB1\Producao\Web\SiagroB1.Web.exe", parsed.Value.ExePath);
        Assert.Equal("Production", parsed.Value.Environment);
        Assert.Equal(55100, parsed.Value.Port);
    }

    [Fact]
    public void Parse_Unquoted_DefaultsEnvironment()
    {
        var parsed = BinPathParser.Parse(@"C:\X\SiagroB1.Gateway.exe --urls http://*:55000");

        Assert.Equal(@"C:\X\SiagroB1.Gateway.exe", parsed!.Value.ExePath);
        Assert.Equal("Production", parsed.Value.Environment);
        Assert.Equal(55000, parsed.Value.Port);
    }

    [Fact]
    public void Parse_MissingUrls_ReturnsNull()
    {
        Assert.Null(BinPathParser.Parse("\"C:\\X\\SiagroB1.Web.exe\" --environment Production"));
    }
}

public class InstallationDetectorTests : IDisposable
{
    private readonly string _base = Directory.CreateTempSubdirectory("siagro-detect").FullName;

    public void Dispose() => Directory.Delete(_base, true);

    private sealed class FakeQuery(params (string Name, string BinPath)[] services) : IServiceQuery
    {
        public IEnumerable<(string Name, string BinPath)> All() => services;
    }

    private string Exe(string folder, string kind) => Path.Combine(_base, folder, $"SiagroB1.{kind}.exe");

    [Fact]
    public void Detect_WithManifest_UsesManifest()
    {
        var manifest = new InstallManifest("1.1.290", "Producao", _base, "Production",
            [new InstalledService(ServiceKind.Web, "siagrob1-producao-web", Exe("Web", "Web"), "Production", 55100)],
            new DateTime(2026, 10, 27));
        manifest.Save(_base);

        var result = new InstallationDetector(new FakeQuery()).Detect(_base);

        Assert.NotNull(result.Manifest);
        Assert.Equal("1.1.290", result.Manifest!.Version);
        Assert.Single(result.Services);
        Assert.False(result.IsLegacy);
    }

    [Fact]
    public void Detect_LegacyScripts_FindsServicesByExeName()
    {
        var query = new FakeQuery(
            ("siagrob1webservice", $"\"{Exe("Web", "Web")}\" --environment Production --urls http://*:55100"),
            ("siagrob1gatewayservice", $"\"{Exe("Gateway", "Gateway")}\" --environment Production --urls http://*:55000"),
            ("siagrob1reportsservice", $"\"{Exe("Reports", "Reports")}\" --environment Production --urls http://*:55200"),
            ("Spooler", @"C:\Windows\System32\spoolsv.exe"));

        var result = new InstallationDetector(query).Detect(_base);

        Assert.True(result.IsLegacy);
        Assert.Null(result.Manifest);
        Assert.Equal(3, result.Services.Count);
        var gateway = result.Services.Single(s => s.Kind == ServiceKind.Gateway);
        Assert.Equal("siagrob1gatewayservice", gateway.ServiceName);
        Assert.Equal(55000, gateway.Port);
    }

    [Fact]
    public void Detect_IgnoresServicesOutsideBasePath()
    {
        var query = new FakeQuery(("outro", "\"D:\\Outro\\Web\\SiagroB1.Web.exe\" --urls http://*:56100"));

        var result = new InstallationDetector(query).Detect(_base);

        Assert.Empty(result.Services);
    }

    [Fact]
    public void Detect_Nothing_ReturnsEmpty()
    {
        var result = new InstallationDetector(new FakeQuery()).Detect(_base);

        Assert.Null(result.Manifest);
        Assert.Empty(result.Services);
        Assert.False(result.IsLegacy);
    }

    [Fact]
    public void Detect_BasePathWithTrailingSlashAndDifferentCase()
    {
        var query = new FakeQuery(("siagrob1webservice", $"\"{Exe("Web", "Web").ToUpperInvariant()}\" --urls http://*:55100"));

        var result = new InstallationDetector(query).Detect(_base + "\\");

        Assert.Single(result.Services);
    }
}
