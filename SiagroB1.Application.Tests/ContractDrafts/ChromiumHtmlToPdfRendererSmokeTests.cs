using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Infra.Pdf;

namespace SiagroB1.Application.Tests.ContractDrafts;

/// <summary>
/// Só roda quando a variável CHROMIUM_PATH aponta para um executável — a suíte não baixa 150 MB.
/// Rode local com: $env:CHROMIUM_PATH = 'C:\Program Files\Google\Chrome\Application\chrome.exe'.
/// </summary>
public class ChromiumHtmlToPdfRendererSmokeTests
{
    [Fact]
    [Trait("Category", "Chromium")]
    public async Task Renders_a_pdf_from_html()
    {
        var path = Environment.GetEnvironmentVariable("CHROMIUM_PATH");
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return; // sem Chromium na máquina: teste vira no-op, não falha.

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [ChromiumHtmlToPdfRenderer.ChromiumPathKey] = path })
            .Build();
        await using var renderer = new ChromiumHtmlToPdfRenderer(configuration, NullLogger<ChromiumHtmlToPdfRenderer>.Instance);

        var pdf = await renderer.RenderAsync("<!DOCTYPE html><html><body><h1>Minuta</h1></body></html>");

        Assert.True(pdf.Length > 1000);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
    }

    [Fact]
    public void IsAvailable_is_false_when_the_configured_path_does_not_exist()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [ChromiumHtmlToPdfRenderer.ChromiumPathKey] =
                    Path.Combine(Path.GetTempPath(), "chrome-que-nao-existe.exe"),
            })
            .Build();

        Assert.False(ChromiumHtmlToPdfRenderer.IsAvailable(configuration));
    }

    [Fact]
    public void IsAvailable_does_not_throw_when_nothing_is_configured()
    {
        // O aviso de boot chama isto antes de o app subir, com a pasta de download normalmente
        // inexistente: uma exceção aqui derruba o serviço inteiro por causa de um aviso.
        var configuration = new ConfigurationBuilder().Build();

        Assert.Null(Record.Exception(() => ChromiumHtmlToPdfRenderer.IsAvailable(configuration)));
    }
}
