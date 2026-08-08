using SiagroB1.Client;

var builder = Host.CreateApplicationBuilder(args);

if (OperatingSystem.IsWindows())
    builder.Services.AddWindowsService();

if (OperatingSystem.IsLinux())
    builder.Services.AddSystemd();

// Uma instância do serviço atende N balanças: uma conexão WebSocket para cada.
var scaleCodes = builder.Configuration.GetSection("TruckScaleIds").Get<string[]>() ?? [];

if (scaleCodes.Length == 0)
    throw new InvalidOperationException("Configure TruckScaleIds no appsettings.");

foreach (var code in scaleCodes)
{
    builder.Services.AddSingleton<IHostedService>(sp => new ScaleWorker(
        code,
        sp.GetRequiredService<IConfiguration>(),
        sp.GetRequiredService<ILogger<ScaleWorker>>()));
}

var host = builder.Build();
host.Run();
