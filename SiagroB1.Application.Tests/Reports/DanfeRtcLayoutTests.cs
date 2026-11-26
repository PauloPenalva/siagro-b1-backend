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

    // ---- Grade de produtos (Task 4) ----

    private static readonly string[] ItemCells =
    [
        "txtRtcItemDescricao", "txtRtcItemCstCfop", "txtRtcItemQtdUn", "txtRtcItemVlrUnit", "txtRtcItemVlrTotal",
        "txtRtcItemBases", "txtRtcItemRatesLeft", "txtRtcItemRatesRight", "txtRtcItemValuesLeft", "txtRtcItemValuesRight",
    ];

    [Fact]
    public void Item_row_prints_the_reform_columns()
    {
        using var report = Service().Prepare(ProcXml(), cancelled: false);
        var texts = Texts(report);
        Assert.Contains("50.638,50", texts["txtRtcItemBases"]);
        Assert.Contains("0,10%", texts["txtRtcItemRatesLeft"]);
        Assert.Contains("0,90%", texts["txtRtcItemRatesRight"]);
        Assert.Contains("50,64", texts["txtRtcItemValuesLeft"]);
        Assert.Contains("455,75", texts["txtRtcItemValuesRight"]);
        Assert.Contains("60.000,00", texts["txtRtcItemVlrTotal"]);
        Assert.Contains("6102", texts["txtRtcItemCstCfop"]);
        Assert.Contains("000", texts["txtRtcItemCstCfop"]);
        Assert.Contains("KG", texts["txtRtcItemQtdUn"]);
    }

    [Fact]
    public void Description_cell_carries_code_ncm_and_classification()
    {
        using var report = Service().Prepare(ProcXml(), cancelled: false);
        var description = Texts(report)["txtRtcItemDescricao"];
        Assert.Contains("SOJA", description);
        Assert.Contains("SOJA EM GRAOS", description);
        Assert.Contains("[NCM 12019000]", description);
        Assert.Contains("[cClassTrib 000001]", description);
    }

    [Fact]
    public void Item_without_ibscbs_omits_the_classification()
    {
        var xml = ProcXml();
        Assert.Contains("<IBSCBS>", xml);
        xml = Regex.Replace(xml, "<IBSCBS>.*?</IBSCBS>", "", RegexOptions.Singleline);

        using var report = Service().Prepare(xml, cancelled: false);
        var description = Texts(report)["txtRtcItemDescricao"];
        Assert.Contains("[NCM 12019000]", description);
        Assert.DoesNotContain("[cClassTrib", description);
    }

    [Fact]
    public void Old_grid_columns_are_gone()
    {
        using var report = Service().Prepare(ProcXml(), cancelled: false);
        using var page = report.PreparedPages.GetPage(0);
        var header = page.AllObjects.OfType<BandBase>().Single(b => b.Name == "DadosProdutosHeader");
        var headerTexts = header.AllObjects.OfType<TextObject>().Select(t => t.Text).ToList();

        Assert.Contains(headerTexts, t => t.Contains("BASES DE CÁLCULO"));
        Assert.DoesNotContain(headerTexts, t => t.Contains("DESCONTO"));
        Assert.DoesNotContain(headerTexts, t => t.Contains("NCM"));
        Assert.DoesNotContain(headerTexts, t => t.Contains("CÓDIGO"));
        Assert.DoesNotContain(headerTexts, t => t.Contains("BC ST") || t.Contains("VALOR ST") || t.Contains("ICMS ST"));
    }

    [Fact]
    public void Each_item_gets_its_own_values()
    {
        var xml = WithItems(ProcXml(), 2, (n, det) => n == 2 ? det.Replace("<vBC>50638.50</vBC>", "<vBC>12345.67</vBC>") : det);

        using var report = Service().Prepare(xml, cancelled: false);
        var bases = AllTexts(report).Where(t => t.Name == "txtRtcItemBases").Select(t => t.Text).ToList();
        Assert.Equal(2, bases.Count);
        Assert.Contains("50.638,50", bases[0]);
        Assert.Contains("12.345,67", bases[1]);
    }

    [Fact]
    public void Many_items_do_not_overlap_the_footer()
    {
        using var report = Service().Prepare(WithItems(ProcXml(), 60), cancelled: false);
        Assert.True(report.PreparedPages.Count > 1);
        Assert.Equal(60, AllTexts(report).Count(t => t.Name == "txtRtcItemDescricao"));

        using var page = report.PreparedPages.GetPage(0);
        var footerTop = page.AllObjects.OfType<BandBase>().Single(b => b.Name == "DadosAdicionais").Top;
        var cells = page.AllObjects.OfType<TextObject>().Where(t => ItemCells.Contains(t.Name)).ToList();
        Assert.NotEmpty(cells);
        var lastBottom = cells.Max(c => c.AbsBottom);
        Assert.True(lastBottom <= footerTop, $"última linha termina em {lastBottom}, rodapé começa em {footerTop}");

        // Prende a quebra da folha 1: com o XML padrão cabem 6 linhas de 40 e a sobra até o rodapé é menor que uma linha.
        var rows = cells.Where(c => c.Name == "txtRtcItemDescricao").ToList();
        Assert.Equal(6, rows.Count);
        var minimumRow = rows.Min(c => c.Height);
        Assert.True(footerTop - lastBottom < minimumRow,
            $"sobram {footerTop - lastBottom} na folha 1, cabe mais uma linha de {minimumRow}");
    }

    [Fact]
    public void Long_description_grows_the_whole_row()
    {
        var longName = string.Concat(Enumerable.Repeat("PRODUTO COM NOME LONGO ", 6))[..120];
        var longInfo = string.Concat(Enumerable.Repeat("INFORMACAO ADICIONAL DO ITEM ", 11))[..300];
        var xml = WithItems(ProcXml(), 3, (n, det) => n == 1
            ? det.Replace("<xProd>SOJA EM GRAOS</xProd>", $"<xProd>{longName}</xProd>").Replace("</imposto>", $"</imposto><infAdProd>{longInfo}</infAdProd>")
            : det);

        using var report = Service().Prepare(xml, cancelled: false);
        var all = AllTexts(report).Where(t => ItemCells.Contains(t.Name)).ToList();
        Assert.Contains(all, t => t.Name == "txtRtcItemDescricao" && t.Text.Contains(longInfo[..40]));

        var rows = all.GroupBy(t => t.Parent).Select(g => g.ToList()).ToList();
        Assert.Equal(3, rows.Count);
        // Todas as células da mesma linha crescem juntas.
        foreach (var row in rows)
            Assert.Single(row.Select(c => Math.Round(c.AbsBottom, 1)).Distinct());
        Assert.True(rows[0].Max(c => c.Height) > rows[1].Max(c => c.Height),"a linha da descrição longa deveria ser mais alta");
        for (var i = 0; i + 1 < rows.Count; i++)
            Assert.True(rows[i].Max(c => c.AbsBottom) <= rows[i + 1].Min(c => c.AbsTop) + 0.01f);
    }

    [Fact]
    public void Large_amounts_fit_their_columns()
    {
        // Pior caso realista de 9 dígitos: base 9.999.999,99, ICMS de 18% (1.799.999,99), CBS de 7 dígitos,
        // alíquota com 4 casas, quantidade de 9 dígitos e valor unitário com 4 casas.
        var xml = WithItems(ProcXml(), 1, (_, det) => det
            .Replace("<vProd>60000.00</vProd>", "<vProd>9999999.99</vProd>")
            .Replace("<vBC>60000.00</vBC>", "<vBC>9999999.99</vBC>")
            .Replace("<pICMS>7.0000</pICMS>", "<pICMS>18.0000</pICMS>")
            .Replace("<vICMS>4200.00</vICMS>", "<vICMS>1799999.99</vICMS>")
            .Replace("<vBC>50638.50</vBC>", "<vBC>9999999.99</vBC>")
            .Replace("<vIBSUF>50.64</vIBSUF>", "<vIBSUF>1234567.89</vIBSUF>")
            .Replace("<pCBS>0.9000</pCBS>", "<pCBS>0.0375</pCBS>")
            .Replace("<vCBS>455.75</vCBS>", "<vCBS>1234567.89</vCBS>")
            .Replace("<qCom>30000.0000</qCom>", "<qCom>9999999.9900</qCom>")
            .Replace("<vUnCom>2.0000000000</vUnCom>", "<vUnCom>12345.6789000000</vUnCom>"));

        using var report = Service().Prepare(xml, cancelled: false);
        var texts = Texts(report);
        Assert.Contains("9.999.999,99", texts["txtRtcItemVlrTotal"]);
        Assert.Contains("9.999.999,99", texts["txtRtcItemBases"]);
        Assert.Contains("1.799.999,99", texts["txtRtcItemValuesLeft"]);
        Assert.Contains("1.234.567,89", texts["txtRtcItemValuesRight"]);
        Assert.Contains("0,0375%", texts["txtRtcItemRatesRight"]);
        Assert.Contains("12.345,6789", texts["txtRtcItemVlrUnit"]);

        using var page = report.PreparedPages.GetPage(0);
        var row = page.AllObjects.OfType<TextObject>()
            .Where(t => t.Name.StartsWith("txtRtcItem") || t.Name.StartsWith("lblRtcItem")).ToList();
        Assert.True(row.Count >= ItemCells.Length);
        // Folga de 4%: no Linux as fontes métricas podem medir um pouco mais largo que o GDI+ do Windows.
        const float slack = 1.04f;
        var clipped = row
            .Select(cell => (cell, needed: MeasuredWidth(report, cell) * slack))
            .Where(x => x.needed > x.cell.Width)
            .Select(x => $"{x.cell.Name}: \"{x.cell.Text.Replace('\n', '|')}\" precisa de {x.needed:0.0}, a coluna tem {x.cell.Width:0.0}")
            .ToList();
        Assert.True(clipped.Count == 0, string.Join(Environment.NewLine, clipped));
    }

    [Fact]
    public void Simples_issuer_prints_csosn()
    {
        var xml = ProcXml();
        Assert.Contains("<CRT>3</CRT>", xml);
        xml = xml.Replace("<CRT>3</CRT>", "<CRT>1</CRT>");
        xml = Regex.Replace(xml, "<ICMS><ICMS00>.*?</ICMS00></ICMS>",
            "<ICMS><ICMSSN102><orig>0</orig><CSOSN>102</CSOSN></ICMSSN102></ICMS>", RegexOptions.Singleline);

        using var report = Service().Prepare(xml, cancelled: false);
        Assert.Contains("0102", Texts(report)["txtRtcItemCstCfop"]);
    }

    /// <summary>
    /// Largura que o texto preparado precisa (sem quebra de linha), medida com a fonte e o padding do objeto,
    /// no próprio objeto de design do relatório.
    /// </summary>
    private static float MeasuredWidth(Report report, TextObject prepared)
    {
        var design = Assert.IsType<TextObject>(report.FindObject(prepared.Name), exactMatch: false);
        var (text, wrap) = (design.Text, design.WordWrap);
        try
        {
            design.Text = prepared.Text;
            design.WordWrap = false;
            return design.CalcWidth();
        }
        finally
        {
            design.Text = text;
            design.WordWrap = wrap;
        }
    }

    /// <summary>Repete o det do XML em <paramref name="count"/> itens (nItem 1..n), com uma alteração opcional por item.</summary>
    private static string WithItems(string xml, int count, Func<int, string, string>? change = null)
    {
        var det = Regex.Match(xml, "<det nItem=\"1\">.*?</det>", RegexOptions.Singleline);
        Assert.True(det.Success);
        var items = string.Concat(Enumerable.Range(1, count).Select(n =>
        {
            var block = det.Value.Replace("<det nItem=\"1\">", $"<det nItem=\"{n}\">");
            return change is null ? block : change(n, block);
        }));
        return xml.Remove(det.Index, det.Length).Insert(det.Index, items);
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
