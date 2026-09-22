using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PuppeteerSharp;
using PuppeteerSharp.Media;
using SiagroB1.Domain.Interfaces;

namespace SiagroB1.Infra.Pdf;

/// <summary>
/// PDF por Chromium headless (PuppeteerSharp). Um browser por processo, criado no primeiro uso;
/// cada render abre e fecha uma página. Registrado como singleton.
///
/// <c>Signature:Pdf:ChromiumPath</c> aponta para o executável; vazio ⇒ <see cref="BrowserFetcher"/>
/// baixa para <c>{ContentRoot}/chromium</c> (precisa de internet no primeiro uso).
/// <c>Signature:Pdf:NoSandbox</c> liga <c>--no-sandbox</c>, necessário em alguns Windows Service e
/// Linux — só ligue quando o log pedir (Chromium ≥ 125 falha o sandbox em serviço sem sessão).
/// </summary>
public sealed class ChromiumHtmlToPdfRenderer(
    IConfiguration configuration,
    ILogger<ChromiumHtmlToPdfRenderer> logger) : IHtmlToPdfRenderer, IAsyncDisposable
{
    public const string ChromiumPathKey = "Signature:Pdf:ChromiumPath";
    public const string NoSandboxKey = "Signature:Pdf:NoSandbox";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private IBrowser? _browser;

    public async Task<byte[]> RenderAsync(string html, CancellationToken ct = default)
    {
        var browser = await GetBrowserAsync(ct);

        await using var page = await browser.NewPageAsync();
        await page.SetContentAsync(html, new SetContentOptions { WaitUntil = [WaitUntilNavigation.Load] });

        return await page.PdfDataAsync(new PdfOptions
        {
            Format = PaperFormat.A4,
            PrintBackground = true,
            MarginOptions = new MarginOptions { Top = "20mm", Bottom = "20mm", Left = "20mm", Right = "20mm" },
        });
    }

    private async Task<IBrowser> GetBrowserAsync(CancellationToken ct)
    {
        if (_browser is { IsClosed: false })
            return _browser;

        await _gate.WaitAsync(ct);
        try
        {
            if (_browser is { IsClosed: false })
                return _browser;

            var executable = await ResolveExecutableAsync();
            var args = configuration.GetValue(NoSandboxKey, false) ? new[] { "--no-sandbox" } : [];

            _browser = await Puppeteer.LaunchAsync(new LaunchOptions
            {
                Headless = true, ExecutablePath = executable, Args = args,
            });

            logger.LogInformation("Chromium iniciado para PDF de minutas: {Executable}", executable);
            return _browser;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<string> ResolveExecutableAsync()
    {
        var configured = configuration[ChromiumPathKey];
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return configured;

        var downloadPath = Path.Combine(AppContext.BaseDirectory, "chromium");
        var fetcher = new BrowserFetcher(new BrowserFetcherOptions { Path = downloadPath });
        var installed = await fetcher.DownloadAsync();

        return installed.GetExecutablePath();
    }

    /// <summary>Só diz se há executável sem baixar nada — para o aviso de boot.</summary>
    public static bool IsAvailable(IConfiguration configuration)
    {
        var configured = configuration[ChromiumPathKey];
        if (!string.IsNullOrWhiteSpace(configured))
            return File.Exists(configured);

        var fetcher = new BrowserFetcher(new BrowserFetcherOptions
            { Path = Path.Combine(AppContext.BaseDirectory, "chromium") });
        return fetcher.GetInstalledBrowsers().Any();
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null)
            await _browser.CloseAsync();
        _gate.Dispose();
    }
}
