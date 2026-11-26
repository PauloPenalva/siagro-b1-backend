using System.Text.RegularExpressions;
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

    [Fact]
    public void Issuer_block_prints_the_tax_regime_and_leaves_the_assessment_regime_empty()
    {
        using var report = Service().Prepare(ProcXml(), cancelled: false);
        var texts = Texts(report);
        Assert.Equal("3 - REGIME NORMAL", texts["txtRtcCrt"]);
        Assert.Equal("", texts["txtRtcRegimeApuracao"]);
    }

    [Fact]
    public void Reform_totals_block_prints_the_four_totals()
    {
        using var report = Service().Prepare(ProcXml(), cancelled: false);
        var texts = Texts(report);
        Assert.Equal("455,75", texts["txtRtcTotalCbs"]);
        Assert.Equal("50,64", texts["txtRtcTotalIbsUf"]);
        Assert.Equal("0,00", texts["txtRtcTotalIbsMun"]);
        Assert.Equal("", texts["txtRtcTotalIs"]);
    }

    [Fact]
    public void Totals_blocks_follow_the_official_order()
    {
        using var report = Service().Prepare(ProcXml(), cancelled: false);
        using var page = report.PreparedPages.GetPage(0);
        float Top(string band) => page.AllObjects.OfType<BandBase>().Single(b => b.Name == band).Top;

        Assert.True(Top("TotalProdutosNota") < Top("Imposto"));
        Assert.True(Top("Imposto") < Top("TotalIbsCbsIs"));
        Assert.True(Top("TotalIbsCbsIs") < Top("TransportadorVolumes"));
    }

    [Fact]
    public void Optional_lines_are_absent_when_their_values_are_zero()
    {
        using var report = Service().Prepare(ProcXml(), cancelled: false);
        var texts = Texts(report);
        Assert.False(texts.ContainsKey("txtRtcTotalFcp"));
        Assert.False(texts.ContainsKey("txtRtcTotalIcmsMono"));
        Assert.False(texts.ContainsKey("txtRtcTotalIbsMono"));
    }

    [Fact]
    public void Fcp_line_appears_with_a_positive_value()
    {
        var xml = ProcXml();
        Assert.Matches("<vFCP>0(\\.00)?</vFCP>", xml);
        xml = Regex.Replace(xml, "<vFCP>0(\\.00)?</vFCP>", "<vFCP>12.34</vFCP>");

        using var report = Service().Prepare(xml, cancelled: false);
        Assert.Equal("12,34", Texts(report)["txtRtcTotalFcp"]);
    }

    // ImpostoIcmsMono é filha de ImpostoFcp: tem de sair mesmo com a linha do FCP oculta.
    [Fact]
    public void Icms_mono_line_appears_even_when_the_fcp_line_is_hidden()
    {
        var xml = ProcXml();
        Assert.Contains("<vIPIDevol>", xml);
        xml = xml.Replace("<vIPIDevol>", "<qBCMono>100.00</qBCMono><vICMSMono>7.50</vICMSMono><vIPIDevol>");

        using var report = Service().Prepare(xml, cancelled: false);
        var texts = Texts(report);
        Assert.False(texts.ContainsKey("txtRtcTotalFcp"));
        Assert.Equal("7,50", texts["txtRtcTotalIcmsMono"]);
    }

    [Fact]
    public void Pis_and_cofins_totals_are_gone()
    {
        using var report = Service().Prepare(ProcXml(), cancelled: false);
        var all = AllTexts(report).Select(t => t.Text).ToList();
        Assert.DoesNotContain(all, t => t.Contains("VALOR DO PIS"));
        Assert.DoesNotContain(all, t => t.Contains("VALOR DO COFINS"));
    }

    [Fact]
    public void Invoice_without_reform_groups_still_renders()
    {
        var xml = ProcXml();
        Assert.Contains("<IBSCBSTot>", xml);
        xml = Regex.Replace(xml, "<IBSCBS>.*?</IBSCBS>", "", RegexOptions.Singleline);
        xml = Regex.Replace(xml, "<IBSCBSTot>.*?</IBSCBSTot>", "", RegexOptions.Singleline);

        using var report = Service().Prepare(xml, cancelled: false);
        Assert.True(report.PreparedPages.Count >= 1);
        Assert.Equal("", Texts(report)["txtRtcTotalCbs"]);
    }

    /// <summary>Texto impresso por nome de objeto (primeira ocorrência: o cabeçalho se repete por página).</summary>
    private static Dictionary<string, string> Texts(Report report)
    {
        var texts = new Dictionary<string, string>();
        foreach (var text in AllTexts(report))
            texts.TryAdd(text.Name, text.Text);
        return texts;
    }

    private static List<TextObject> AllTexts(Report report) =>
        Enumerable.Range(0, report.PreparedPages.Count)
            .SelectMany(i => report.PreparedPages.GetPage(i).AllObjects.OfType<TextObject>())
            .ToList();

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
