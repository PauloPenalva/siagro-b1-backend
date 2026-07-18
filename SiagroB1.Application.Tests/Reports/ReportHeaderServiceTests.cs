using FastReport;
using FastReport.Data;
using Microsoft.Extensions.Configuration;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Reports.Services;

namespace SiagroB1.Application.Tests.Reports;

/// <summary>
/// ReportHeaderService applies the company identity (name + logo) to an already
/// loaded report. It must never break report generation when the logo is missing
/// or when the template has no picLogo object.
/// </summary>
public class ReportHeaderServiceTests : IDisposable
{
    // 1x1 transparent PNG - enough for FastReport/System.Drawing to decode.
    private const string OnePixelPngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";

    private readonly string _contentRoot;

    public ReportHeaderServiceTests()
    {
        _contentRoot = Path.Combine(Path.GetTempPath(), "siagro-report-header-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_contentRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_contentRoot))
            Directory.Delete(_contentRoot, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Apply_SetsCompanyNameParameter_FromConfiguration()
    {
        var service = CreateService(companyName: "COMERCIO DE CEREAIS YOKOTOBI LTDA", logoPath: null);
        using var report = CreateReport(withLogoObject: true);

        service.Apply(report);

        Assert.Equal("COMERCIO DE CEREAIS YOKOTOBI LTDA", report.GetParameterValue("pCompanyName"));
    }

    [Fact]
    public void Apply_WithoutCompanyNameConfigured_LeavesParameterEmpty()
    {
        var service = CreateService(companyName: null, logoPath: null);
        using var report = CreateReport(withLogoObject: true);

        service.Apply(report);

        Assert.Equal("", report.GetParameterValue("pCompanyName"));
    }

    [Fact]
    public void Apply_WithRelativeLogoPath_ResolvesAgainstContentRoot()
    {
        var logoFile = WriteLogo(Path.Combine("wwwroot", "images", "logo.png"));
        Assert.StartsWith(_contentRoot, logoFile); // guards the fixture itself

        var service = CreateService(companyName: "ACME", logoPath: "wwwroot/images/logo.png");
        using var report = CreateReport(withLogoObject: true);

        service.Apply(report);

        Assert.NotNull(GetLogo(report)!.Image);
    }

    [Fact]
    public void Apply_WithAbsoluteLogoPath_UsesItAsIs()
    {
        var logoFile = WriteLogo(Path.Combine("assets", "brand.png"));

        var service = CreateService(companyName: "ACME", logoPath: logoFile);
        using var report = CreateReport(withLogoObject: true);

        service.Apply(report);

        Assert.NotNull(GetLogo(report)!.Image);
    }

    [Fact]
    public void Apply_WithMissingLogoFile_DoesNotThrowAndLeavesImageNull()
    {
        var service = CreateService(companyName: "ACME", logoPath: "wwwroot/images/does-not-exist.png");
        using var report = CreateReport(withLogoObject: true);

        service.Apply(report);

        Assert.Null(GetLogo(report)!.Image);
        Assert.Equal("ACME", report.GetParameterValue("pCompanyName"));
    }

    /// <summary>
    /// The decode happens eagerly inside Apply precisely so that an image the platform
    /// cannot handle degrades to "report without logo" instead of taking the report down.
    /// This is the failure mode to expect on Linux, where FastReport draws through
    /// System.Drawing.Common 4.7.3 / libgdiplus rather than Windows GDI+.
    /// </summary>
    [Fact]
    public void Apply_WithUndecodableLogoFile_DoesNotThrowAndStillSetsCompanyName()
    {
        var logoFile = Path.Combine(_contentRoot, "wwwroot", "images", "logo.png");
        Directory.CreateDirectory(Path.GetDirectoryName(logoFile)!);
        File.WriteAllText(logoFile, "this is not an image");

        var service = CreateService(companyName: "ACME", logoPath: "wwwroot/images/logo.png");
        using var report = CreateReport(withLogoObject: true);

        service.Apply(report);

        Assert.Null(GetLogo(report)!.Image);
        Assert.Equal("ACME", report.GetParameterValue("pCompanyName"));
    }

    [Fact]
    public void Apply_WithoutLogoPathConfigured_DoesNotThrow()
    {
        var service = CreateService(companyName: "ACME", logoPath: null);
        using var report = CreateReport(withLogoObject: true);

        service.Apply(report);

        Assert.Null(GetLogo(report)!.Image);
    }

    [Fact]
    public void Apply_OnReportWithoutLogoObject_DoesNotThrow()
    {
        WriteLogo(Path.Combine("wwwroot", "images", "logo.png"));

        var service = CreateService(companyName: "ACME", logoPath: "wwwroot/images/logo.png");
        using var report = CreateReport(withLogoObject: false);

        service.Apply(report);

        Assert.Equal("ACME", report.GetParameterValue("pCompanyName"));
    }

    [Fact]
    public void Apply_OnTwoReports_GivesEachItsOwnImageInstance()
    {
        WriteLogo(Path.Combine("wwwroot", "images", "logo.png"));

        var service = CreateService(companyName: "ACME", logoPath: "wwwroot/images/logo.png");
        using var first = CreateReport(withLogoObject: true);
        using var second = CreateReport(withLogoObject: true);

        service.Apply(first);
        service.Apply(second);

        var firstImage = GetLogo(first)!.Image;
        var secondImage = GetLogo(second)!.Image;

        Assert.NotNull(firstImage);
        Assert.NotNull(secondImage);
        // Disposing one report must not corrupt the other's image.
        Assert.NotSame(firstImage, secondImage);
    }

    private ReportHeaderService CreateService(string? companyName, string? logoPath)
    {
        var settings = new Dictionary<string, string?>();
        if (companyName is not null) settings["CompanyName"] = companyName;
        if (logoPath is not null) settings["CompanyLogoPath"] = logoPath;

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        return new ReportHeaderService(new TestWebHostEnvironment(_contentRoot), configuration, new TestLogger<ReportHeaderService>());
    }

    private string WriteLogo(string relativePath)
    {
        var fullPath = Path.Combine(_contentRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllBytes(fullPath, Convert.FromBase64String(OnePixelPngBase64));
        return fullPath;
    }

    private static PictureObject? GetLogo(Report report) => report.FindObject("picLogo") as PictureObject;

    private static Report CreateReport(bool withLogoObject)
    {
        var report = new Report();
        var page = new ReportPage { Name = "Page1" };
        report.Pages.Add(page);

        var band = new PageHeaderBand { Name = "PageHeader1", Height = 50 };
        page.Bands.Add(band);

        if (withLogoObject)
            band.Objects.Add(new PictureObject { Name = "picLogo" });

        report.Parameters.Add(new Parameter { Name = "pCompanyName", DataType = typeof(string) });

        return report;
    }
}
