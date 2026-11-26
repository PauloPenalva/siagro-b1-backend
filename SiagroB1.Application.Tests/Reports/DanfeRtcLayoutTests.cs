using FastReport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Fiscal.Certificates;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Fiscal.Tests.Support;
using SiagroB1.Infra;
using SiagroB1.Reports.Services;

namespace SiagroB1.Application.Tests.Reports;

/// <summary>O DANFE sai do leiaute derivado NFeRetratoRtc.frx, com os parâmetros da Reforma Tributária.</summary>
public class DanfeRtcLayoutTests
{
    // SHA-256 do NFeRetrato.frx da Zeus (commit 08743cd): a obra original não pode ser editada.
    private const string ZeusLayoutSha256 = "135407b8b15887c4746d02204874d87de109739f97755cb2d3f3d6fa5028c291";

    private static readonly string ContentRoot = Path.Combine(AppContext.BaseDirectory, "ReportsContentRoot");

    [Fact]
    public void Report_receives_the_reform_parameters()
    {
        using var report = Service().Prepare(ProcXml(), cancelled: false);
        Assert.Equal("3 - REGIME NORMAL", report.GetParameterValue("RtcCrt"));
        Assert.Single((string[])report.GetParameterValue("RtcItemBases"));
    }

    [Fact]
    public void Derived_layout_is_the_one_loaded()
    {
        using var report = Service().Prepare(ProcXml(), cancelled: false);
        Assert.EndsWith("NFeRetratoRtc.frx", report.FileName);
        Assert.True(File.Exists(report.FileName));
    }

    [Fact]
    public void Original_zeus_layout_is_untouched()
    {
        var path = Path.Combine(ContentRoot, "ThirdParty", "Zeus-LGPL", "NFe.Danfe.Base", "NFe", "NFeRetrato.frx");
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
        Assert.Equal(ZeusLayoutSha256, hash, ignoreCase: true);
    }

    private static string ProcXml()
    {
        using var certificate = CertificateLoader.Load(TestCertificates.CreatePfx(), TestCertificates.Password);
        var settings = new NfeServiceSettings(NfeEnvironment.Homologation, "SP", certificate, NfeServiceSettings.DefaultSchemasDirectory);
        var signed = NfeSigner.BuildSignAndValidate(NfeTestData.Input(), settings);
        return NfeProcComposer.Compose(signed.Xml, FakeNfeSefazClient.Authorized(signed.AccessKey).ProtocolXml!);
    }

    private static DanfeReportService Service()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["CompanyLogoPath"] = "wwwroot/images/logo.png" })
            .Build();
        var environment = new TestWebHostEnvironment(ContentRoot);
        var header = new ReportHeaderService(environment, configuration, NullLogger<ReportHeaderService>.Instance, FakeCompanyContext.Unresolved());
        return new DanfeReportService(TestDb.CreateUnitOfWork(), environment, header);
    }
}
