using System.Collections.Concurrent;
using System.Drawing;
using FastReport;
using SiagroB1.Infra.Companies;

namespace SiagroB1.Reports.Services;

/// <summary>
/// Applies the company identity (name + logo) to an already loaded report.
///
/// Templates opt in by declaring a "pCompanyName" parameter and/or a PictureObject
/// named "picLogo" in their page header. Anything missing is silently skipped -
/// a misconfigured logo must never stop a report from being generated.
/// </summary>
public class ReportHeaderService(
    IWebHostEnvironment env,
    IConfiguration configuration,
    ILogger<ReportHeaderService> logger,
    ICompanyContext companyContext)
{
    public const string CompanyNameParameter = "pCompanyName";
    public const string LogoObjectName = "picLogo";

    // Bytes por CAMINHO: duas empresas com logos diferentes não podem ficar se expulsando de um
    // cache de um slot só. Só caminhos encontrados entram; arquivo ausente é reconsultado.
    // The bytes are cached, not the Image: System.Drawing images are not safe to
    // share between reports rendering concurrently, so each report gets its own.
    private static readonly ConcurrentDictionary<string, byte[]> LogoCache = new(StringComparer.OrdinalIgnoreCase);

    public void Apply(Report report)
    {
        report.SetParameterValue(CompanyNameParameter,
            companyContext.Current?.CompanyName ?? configuration.GetValue<string>("CompanyName") ?? "");

        if (report.FindObject(LogoObjectName) is not PictureObject picLogo)
            return;

        var logo = LoadLogo();
        if (logo is null)
            return;

        try
        {
            // Decoding here, rather than handing the raw bytes to PictureObject.SetImageData,
            // is deliberate: it keeps any imaging failure inside this try/catch. On Linux
            // FastReport draws through System.Drawing.Common 4.7.3 / libgdiplus, so a logo
            // the platform cannot decode must cost us the logo, never the whole report.
            using var stream = new MemoryStream(logo, writable: false);
            using var decoded = Image.FromStream(stream);
            // Copy into a standalone Bitmap: an Image created from a stream keeps reading
            // from it lazily, and FastReport serializes the image long after Apply returns.
            picLogo.Image = new Bitmap(decoded);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not decode the company logo; the report will be generated without it.");
        }
    }

    /// <summary>Bytes do logo configurado (ou nulo) — para layouts que recebem a imagem crua, como o DANFE.</summary>
    public byte[]? LogoBytes() => LoadLogo();

    private byte[]? LoadLogo()
    {
        var candidates = LogoCandidates().ToList();
        foreach (var fullPath in candidates)
        {
            if (LogoCache.TryGetValue(fullPath, out var cached))
                return cached;

            if (!File.Exists(fullPath))
                continue;

            var bytes = File.ReadAllBytes(fullPath);
            LogoCache[fullPath] = bytes;
            return bytes;
        }

        if (candidates.Count > 0)
            logger.LogWarning("Logo da empresa não encontrado em {Paths}; o relatório sai sem logo.", string.Join(" | ", candidates));

        return null;
    }

    /// <summary>Convenção por empresa (wwwroot/images/logo-{Code}.png) primeiro; depois CompanyLogoPath.</summary>
    private IEnumerable<string> LogoCandidates()
    {
        var code = companyContext.Current?.Code;
        if (!string.IsNullOrWhiteSpace(code))
            yield return Path.Combine(env.ContentRootPath, "wwwroot", "images", $"logo-{code}.png");

        var configured = configuration.GetValue<string>("CompanyLogoPath");
        if (!string.IsNullOrWhiteSpace(configured))
            yield return Path.IsPathRooted(configured) ? configured : Path.Combine(env.ContentRootPath, configured);
    }
}
