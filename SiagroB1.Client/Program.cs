using SiagroB1.Client;
using SiagroB1.Client.Interfaces;
using SiagroB1.Client.Mock;
using SiagroB1.Client.Readers;

var builder = Host.CreateApplicationBuilder(args);

if (OperatingSystem.IsWindows())
    builder.Services.AddWindowsService();

if (OperatingSystem.IsLinux())
    builder.Services.AddSystemd();

var useMock = builder.Configuration.GetValue<bool>("UseMockScale");

if (useMock)
    builder.Services.AddSingleton<IScaleReader, MockScaleReader>();
else
    builder.Services.AddSingleton<IScaleReader, TcpScaleReader>();

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();