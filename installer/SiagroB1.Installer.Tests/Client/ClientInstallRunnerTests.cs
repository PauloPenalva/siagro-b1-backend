using System.IO.Compression;
using SiagroB1.Installer.Core.Client;
using SiagroB1.Installer.Core.Services;

namespace SiagroB1.Installer.Tests.Client;

public class ClientInstallRunnerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    private readonly FakeServices _services = new();
    private readonly List<string> _log = [];

    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    private static MemoryStream Zip(bool withExe = true)
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            if (withExe) { using var w = new StreamWriter(zip.CreateEntry(ClientInstall.ExeName).Open()); w.Write("exe"); }
        }
        ms.Position = 0;
        return ms;
    }

    private ClientInstallSettings Settings() => new("TS01", "ws://srv:55000/ws/truck-scale", "k", _dir);

    [Fact]
    public async Task Install_extracts_writes_config_registers_and_starts()
    {
        await new ClientInstallRunner(_services, _log.Add).InstallAsync(Settings(), Zip(), default);

        Assert.True(File.Exists(Path.Combine(_dir, ClientInstall.ExeName)));
        Assert.True(File.Exists(Path.Combine(_dir, "appsettings.json")));
        Assert.Equal($"\"{Path.Combine(_dir, ClientInstall.ExeName)}\"", _services.BinPath);
        Assert.Equal(["create", "start"], _services.Calls);
    }

    [Fact]
    public async Task Reinstall_stops_the_running_service_first()
    {
        _services.Installed = true;
        await new ClientInstallRunner(_services, _log.Add).InstallAsync(Settings(), Zip(), default);
        Assert.Equal(["stop", "create", "start"], _services.Calls);
    }

    [Fact]
    public async Task Invalid_settings_touch_nothing()
    {
        var bad = Settings() with { ClientKey = "" };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ClientInstallRunner(_services, _log.Add).InstallAsync(bad, Zip(), default));
        Assert.Empty(_services.Calls);
        Assert.False(Directory.Exists(_dir));
    }

    [Fact]
    public async Task Package_without_exe_fails_before_registering_the_service()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            new ClientInstallRunner(_services, _log.Add).InstallAsync(Settings(), Zip(withExe: false), default));
        Assert.DoesNotContain("create", _services.Calls);
    }

    [Fact]
    public async Task Uninstall_removes_service_and_keeps_files()
    {
        _services.Installed = true;
        Directory.CreateDirectory(_dir);
        await new ClientInstallRunner(_services, _log.Add).UninstallAsync(default);
        Assert.Equal(["stop", "delete"], _services.Calls);
        Assert.True(Directory.Exists(_dir));
    }

    private sealed class FakeServices : IServiceManager
    {
        public bool Installed { get; set; }
        public string? BinPath { get; private set; }
        public List<string> Calls { get; } = [];
        public bool Exists(string name) => Installed;
        public bool IsRunning(string name) => Installed;
        public Task StopAsync(string name, TimeSpan timeout, CancellationToken ct) { Calls.Add("stop"); return Task.CompletedTask; }
        public Task StartAsync(string name, TimeSpan timeout, CancellationToken ct) { Calls.Add("start"); return Task.CompletedTask; }
        public Task CreateOrReplaceAsync(string name, string binPath, string displayName, string description, CancellationToken ct)
        { Calls.Add("create"); BinPath = binPath; return Task.CompletedTask; }
        public Task DeleteAsync(string name, CancellationToken ct) { Calls.Add("delete"); return Task.CompletedTask; }
    }
}
