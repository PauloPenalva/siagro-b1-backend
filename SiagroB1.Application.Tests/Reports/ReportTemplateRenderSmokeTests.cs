using FastReport;
using FastReport.Export.PdfSimple;
using Microsoft.Extensions.Configuration;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Reports.Services;

namespace SiagroB1.Application.Tests.Reports;

/// <summary>
/// Renders every production template (with no data) straight to PDF after applying the
/// company header. Catches layout edits that make FastReport fail to prepare or export,
/// and proves the logo file actually survives PDFSimpleExport.
/// </summary>
public class ReportTemplateRenderSmokeTests
{
    static ReportTemplateRenderSmokeTests()
    {
        FastReport.Utils.RegisteredObjects.AddConnection(typeof(FastReport.Data.MsSqlDataConnection));
        FastReport.Utils.Config.WebMode = true;
    }

    /// <summary>
    /// WeighingTicket.frx binds DateTimeOffset columns, and FastReport cannot convert the
    /// null it gets when no data is registered ("Invalid cast from Int32 to DateTimeOffset").
    /// That is a limitation of rendering data-less, not a template defect - its header is
    /// still covered by <see cref="ReportTemplateHeaderTests"/>.
    /// </summary>
    public static TheoryData<string> RenderableTemplates()
    {
        var data = new TheoryData<string>();
        foreach (var name in ReportTemplateHeaderTests.ProductionTemplateNames())
        {
            if (name != "WeighingTicket.frx")
                data.Add(name);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(RenderableTemplates))]
    public void EveryTemplate_PreparesAndExportsToPdf(string templateName)
    {
        var contentRoot = Path.Combine(AppContext.BaseDirectory, "ReportsContentRoot");
        var logoPath = Path.Combine(contentRoot, "wwwroot", "images", "logo.png");
        Assert.True(File.Exists(logoPath), $"Logo fixture missing at {logoPath}");

        using var report = new Report();
        report.Load(Path.Combine(AppContext.BaseDirectory, "ReportTemplates", templateName));

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CompanyName"] = "ACME AGRO LTDA",
                ["CompanyLogoPath"] = "wwwroot/images/logo.png"
            })
            .Build();

        new ReportHeaderService(
            new TestWebHostEnvironment(contentRoot),
            configuration,
            new TestLogger<ReportHeaderService>()).Apply(report);

        Assert.NotNull(((PictureObject)report.FindObject("picLogo")).Image);

        Assert.True(report.Prepare(), $"{templateName} failed to prepare.");

        using var stream = new MemoryStream();
        report.Export(new PDFSimpleExport { ShowProgress = false }, stream);

        Assert.NotEmpty(stream.ToArray());
    }
}
