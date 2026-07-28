using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SiagroB1.Gateway.Controllers;

/// <summary>
/// Informa qual versão do frontend está publicada no servidor.
///
/// O app consulta este endpoint periodicamente para perceber que houve um deploy e
/// se atualizar sozinho - antes disso, o usuário só via a versão nova depois de um
/// Ctrl+F5, que muitos esqueciam de dar.
/// </summary>
[ApiController]
[Route("security/app")]
[AllowAnonymous]
public class AppVersionController(
    IWebHostEnvironment environment,
    ILogger<AppVersionController> logger
    ) : ControllerBase
{
    private const string CacheBusterFileName = "sap-ui-cachebuster-info.json";
    private const string ManifestFileName = "manifest.json";

    /// <summary>
    /// Última versão lida, indexada pela data de gravação do arquivo de cache buster.
    /// Evita reabrir e reinterpretar o manifest.json a cada consulta - o conteúdo só
    /// pode ter mudado se houve um novo deploy.
    /// </summary>
    private static (long BuildTicks, string Version)? cached;

    [HttpGet("version")]
    public IActionResult GetVersion()
    {
        Response.Headers.CacheControl = "no-store";

        var webRoot = environment.WebRootPath;
        var cacheBusterPath = string.IsNullOrEmpty(webRoot)
            ? null
            : Path.Combine(webRoot, CacheBusterFileName);

        // wwwroot vazio é o cenário de desenvolvimento, em que o frontend roda no dev
        // server do UI5. Sem BuildId o cliente não tem o que comparar e simplesmente
        // nunca dispara uma atualização.
        if (cacheBusterPath is null || !System.IO.File.Exists(cacheBusterPath))
        {
            return Ok(new { Version = GetAssemblyVersion(), BuildId = (string?)null });
        }

        var buildTicks = System.IO.File.GetLastWriteTimeUtc(cacheBusterPath).Ticks;
        var snapshot = cached;

        if (snapshot?.BuildTicks != buildTicks)
        {
            snapshot = (buildTicks, ReadManifestVersion(Path.Combine(webRoot, ManifestFileName)));
            cached = snapshot;
        }

        return Ok(new
        {
            snapshot.Value.Version,
            BuildId = buildTicks.ToString(CultureInfo.InvariantCulture)
        });
    }

    /// <summary>
    /// Lê <c>sap.app.applicationVersion.version</c> do manifest publicado - o build do
    /// UI5 já troca ali o <c>${version}</c> pela versão do package.json.
    /// </summary>
    private string ReadManifestVersion(string manifestPath)
    {
        try
        {
            using var stream = System.IO.File.OpenRead(manifestPath);
            using var document = JsonDocument.Parse(stream, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });

            if (document.RootElement.TryGetProperty("sap.app", out var sapApp) &&
                sapApp.TryGetProperty("applicationVersion", out var applicationVersion) &&
                applicationVersion.TryGetProperty("version", out var version))
            {
                return version.GetString() ?? GetAssemblyVersion();
            }

            logger.LogWarning("Versão não encontrada em {ManifestPath}.", manifestPath);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Falha ao ler a versão de {ManifestPath}.", manifestPath);
        }

        return GetAssemblyVersion();
    }

    private static string GetAssemblyVersion() =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";
}
