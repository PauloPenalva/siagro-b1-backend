using System.Drawing;
using FastReport;

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
    ILogger<ReportHeaderService> logger)
{
    public const string CompanyNameParameter = "pCompanyName";
    public const string LogoObjectName = "picLogo";

    // The bytes are cached, not the Image: System.Drawing images are not safe to
    // share between reports rendering concurrently, so each report gets its own.
    private static byte[]? _cachedLogo;
    private static string? _cachedLogoPath;
    private static readonly Lock CacheLock = new();

    public void Apply(Report report)
    {
        report.SetParameterValue(CompanyNameParameter, configuration.GetValue<string>("CompanyName") ?? "");

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

    private byte[]? LoadLogo()
    {
        var configuredPath = configuration.GetValue<string>("CompanyLogoPath");
        if (string.IsNullOrWhiteSpace(configuredPath))
            return null;

        var fullPath = Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(env.ContentRootPath, configuredPath);

        lock (CacheLock)
        {
            if (_cachedLogo is not null && _cachedLogoPath == fullPath)
                return _cachedLogo;

            if (!File.Exists(fullPath))
            {
                logger.LogWarning("Company logo not found at {LogoPath}; reports will be generated without it.", fullPath);
                return null;
            }

            _cachedLogo = File.ReadAllBytes(fullPath);
            _cachedLogoPath = fullPath;
            return _cachedLogo;
        }
    }
}
