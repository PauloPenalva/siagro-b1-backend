using FastReport;
using Microsoft.Extensions.Configuration;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Reports.Services;

namespace SiagroB1.Application.Tests.Reports;

/// <summary>
/// Loads the real .frx templates shipped with SiagroB1.Reports and checks that every
/// one of them opts into the company header. Guards against a template being added or
/// edited without the picLogo object / pCompanyName parameter.
/// </summary>
public class ReportTemplateHeaderTests
{
    static ReportTemplateHeaderTests()
    {
        // SalesInvoices.frx declares an MsSqlDataConnection; without this the template
        // fails to deserialize. SiagroB1.Reports does the same in Program.cs at startup.
        FastReport.Utils.RegisteredObjects.AddConnection(typeof(FastReport.Data.MsSqlDataConnection));
    }

    // Scaffold with no bands and no code loading it; Teste.frx is excluded from the build.
    private static readonly string[] NotProductionTemplates = ["PrePurchaseContractBlank.frx", "Teste.frx"];

    public static IEnumerable<string> ProductionTemplateNames() =>
        Directory.GetFiles(TemplatesDirectory, "*.frx")
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(name => !NotProductionTemplates.Contains(name));

    public static TheoryData<string> ProductionTemplates()
    {
        var data = new TheoryData<string>();
        foreach (var name in ProductionTemplateNames())
            data.Add(name);

        return data;
    }

    [Theory]
    [MemberData(nameof(ProductionTemplates))]
    public void EveryTemplate_HasLogoObjectAndAcceptsCompanyName(string templateName)
    {
        using var report = new Report();
        report.Load(Path.Combine(TemplatesDirectory, templateName));

        CreateService().Apply(report);

        Assert.NotNull(report.FindObject("picLogo") as PictureObject);
        Assert.Equal("ACME AGRO", report.GetParameterValue("pCompanyName"));
    }

    [Fact]
    public void ProductionTemplates_AreDiscovered()
    {
        // Fails loudly if TemplatesDirectory stops resolving, which would make the
        // theory above silently vacuous.
        Assert.NotEmpty(ProductionTemplates());
    }

    private static string TemplatesDirectory =>
        Path.Combine(AppContext.BaseDirectory, "ReportTemplates");

    private static ReportHeaderService CreateService()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CompanyName"] = "ACME AGRO"
            })
            .Build();

        return new ReportHeaderService(
            new TestWebHostEnvironment(AppContext.BaseDirectory),
            configuration,
            new TestLogger<ReportHeaderService>());
    }
}
