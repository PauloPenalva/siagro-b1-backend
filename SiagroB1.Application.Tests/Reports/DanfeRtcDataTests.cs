using System.Globalization;
using NFe.Classes;
using NFe.Classes.Informacoes.Detalhe.Tributacao.Compartilhado.InformacoesIbsCbs;
using NFe.Classes.Informacoes.Detalhe.Tributacao.Federal;
using NFe.Classes.Informacoes.Emitente;
using NFe.Classes.Informacoes.Total;
using NFe.Classes.Informacoes.Total.IbsCbs.Monofasica;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Fiscal.Certificates;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Fiscal.Tests.Support;
using SiagroB1.Reports.Services;

namespace SiagroB1.Application.Tests.Reports;

/// <summary>Os textos de IBS/CBS/IS do DANFE da NT 2026.010 saem do procNFe autorizado, já formatados em pt-BR.</summary>
public class DanfeRtcDataTests
{
    /// <summary>nfeProc do emissor, desserializado: cada chamada devolve um objeto novo, livre para ser alterado.</summary>
    private static nfeProc Proc()
    {
        using var certificate = CertificateLoader.Load(TestCertificates.CreatePfx(), TestCertificates.Password);
        var settings = new NfeServiceSettings(NfeEnvironment.Homologation, "SP", certificate, NfeServiceSettings.DefaultSchemasDirectory);
        var signed = NfeSigner.BuildSignAndValidate(NfeTestData.Input(), settings);
        var procXml = NfeProcComposer.Compose(signed.Xml, FakeNfeSefazClient.Authorized(signed.AccessKey).ProtocolXml!);
        return new nfeProc().CarregarDeXmlString(procXml);
    }

    [Fact]
    public void Standard_item_prints_base_rates_and_values()
    {
        var item = DanfeRtcData.From(Proc()).Items[0];

        Assert.Equal("000001", item.ClassTrib);
        Assert.Equal("50.638,50", item.Bases.Split('\n')[1]);
        Assert.Equal("", item.Bases.Split('\n')[2]);                       // IS ausente
        Assert.Equal(["0,10%", "0,00%"], item.RatesLeft.Split('\n')[1..]); // IBS UF, IBS Mun (grupo presente, zero impresso)
        Assert.Equal("0,90%", item.RatesRight.Split('\n')[0]);
        Assert.Equal("50,64", item.ValuesLeft.Split('\n')[1]);
        Assert.Equal("455,75", item.ValuesRight.Split('\n')[0]);
    }

    [Fact]
    public void Icms_and_ipi_are_blank_when_the_cst_group_lacks_them()
    {
        var proc = Proc();
        proc.NFe.infNFe.det[0].imposto.IPI = null;

        var item = DanfeRtcData.From(proc).Items[0];

        Assert.Equal("", item.Bases.Split('\n')[3]);
        Assert.Equal("", item.RatesRight.Split('\n')[1]);
        Assert.Equal("", item.ValuesRight.Split('\n')[1]);
    }

    [Fact]
    public void Icms_comes_from_the_item_icms_group()
    {
        var proc = Proc();
        var tipo = proc.NFe.infNFe.det[0].imposto.ICMS.TipoICMS;
        var vBc = (decimal?)tipo.GetType().GetProperty("vBC")?.GetValue(tipo);

        var lines = DanfeRtcData.From(proc).Items[0].Bases.Split('\n');

        Assert.Equal(vBc is { } v ? v.ToString("N2", CultureInfo.GetCultureInfo("pt-BR")) : "", lines[0]);
    }

    [Fact]
    public void Reduction_prints_the_effective_rate()
    {
        var proc = Proc();
        proc.NFe.infNFe.det[0].imposto.IBSCBS.gIBSCBS.gCBS.gRed = new gRed { pRedAliq = 60m, pAliqEfet = 0.36m };

        Assert.Equal("0,36%", DanfeRtcData.From(proc).Items[0].RatesRight.Split('\n')[0]);
    }

    [Fact]
    public void Zero_effective_rate_is_printed_as_zero()
    {
        var proc = Proc();
        proc.NFe.infNFe.det[0].imposto.IBSCBS.gIBSCBS.gIBSUF.gRed = new gRed { pRedAliq = 100m, pAliqEfet = 0m };

        Assert.Equal("0,00%", DanfeRtcData.From(proc).Items[0].RatesLeft.Split('\n')[1]);
    }

    [Fact]
    public void Four_decimal_rate_is_not_rounded()
    {
        var proc = Proc();
        proc.NFe.infNFe.det[0].imposto.IBSCBS.gIBSCBS.gCBS.gRed = new gRed { pRedAliq = 95.8333m, pAliqEfet = 0.0375m };

        Assert.Equal("0,0375%", DanfeRtcData.From(proc).Items[0].RatesRight.Split('\n')[0]);
    }

    [Fact]
    public void Item_without_ibscbs_is_blank()
    {
        var proc = Proc();
        proc.NFe.infNFe.det[0].imposto.IBSCBS = null;

        var item = DanfeRtcData.From(proc).Items[0];

        Assert.Equal("", item.ClassTrib);
        Assert.Equal("", item.Bases.Split('\n')[1]);
        Assert.Equal(["", ""], item.RatesLeft.Split('\n')[1..]);
        Assert.Equal("", item.RatesRight.Split('\n')[0]);
        Assert.Equal(["", ""], item.ValuesLeft.Split('\n')[1..]);
        Assert.Equal("", item.ValuesRight.Split('\n')[0]);
    }

    [Fact]
    public void Missing_municipal_group_is_blank()
    {
        var proc = Proc();
        proc.NFe.infNFe.det[0].imposto.IBSCBS.gIBSCBS.gIBSMun = null;

        var item = DanfeRtcData.From(proc).Items[0];

        Assert.Equal("", item.RatesLeft.Split('\n')[2]);
        Assert.Equal("", item.ValuesLeft.Split('\n')[2]);
    }

    [Fact]
    public void Selective_tax_is_printed_when_present()
    {
        var proc = Proc();
        proc.NFe.infNFe.det[0].imposto.IS = new IS { vBCIS = 100m, pIS = 5m, vIS = 5m };

        var item = DanfeRtcData.From(proc).Items[0];

        Assert.Equal("100,00", item.Bases.Split('\n')[2]);
        Assert.Equal("5,00%", item.RatesRight.Split('\n')[2]);
        Assert.Equal("5,00", item.ValuesRight.Split('\n')[2]);
    }

    [Fact]
    public void Totals_come_from_ibscbstot()
    {
        var data = DanfeRtcData.From(Proc());

        Assert.Equal("455,75", data.TotalCbs);
        Assert.Equal("50,64", data.TotalIbsUf);
        Assert.Equal("0,00", data.TotalIbsMun);
        Assert.Equal("", data.TotalIs);
    }

    [Fact]
    public void Selective_total_is_printed_when_present()
    {
        var proc = Proc();
        proc.NFe.infNFe.total.ISTot = new ISTot { vIS = 5m };

        Assert.Equal("5,00", DanfeRtcData.From(proc).TotalIs);
    }

    [Fact]
    public void Invoice_without_reform_groups_has_blank_totals()
    {
        var proc = Proc();
        proc.NFe.infNFe.total.IBSCBSTot = null;

        var data = DanfeRtcData.From(proc);

        Assert.Equal("", data.TotalCbs);
        Assert.Equal("", data.TotalIbsUf);
        Assert.Equal("", data.TotalIbsMun);
        Assert.Equal("", data.TotalIs);
    }

    [Fact]
    public void Zeroed_fcp_hides_the_optional_line()
    {
        var data = DanfeRtcData.From(Proc());

        Assert.False(data.ShowFcpLine);
        Assert.Equal("", data.TotalFcp);
    }

    [Fact]
    public void Positive_fcp_shows_the_optional_line()
    {
        var proc = Proc();
        proc.NFe.infNFe.total.ICMSTot.vFCP = 12.34m;

        var data = DanfeRtcData.From(proc);

        Assert.True(data.ShowFcpLine);
        Assert.Equal("12,34", data.TotalFcp);
    }

    [Fact]
    public void Monophase_line_shows_only_with_a_positive_value()
    {
        var proc = Proc();
        var tot = proc.NFe.infNFe.total.IBSCBSTot;
        tot.gMono = new gMono();
        Assert.False(DanfeRtcData.From(proc).ShowIbsCbsMonoLine);

        tot.gMono = new gMono { vIBSMono = 12.3m };
        var data = DanfeRtcData.From(proc);

        Assert.True(data.ShowIbsCbsMonoLine);
        Assert.Equal("12,30", data.TotalIbsMono);
    }

    [Fact]
    public void Icms_monophase_line_shows_only_with_a_positive_value()
    {
        var proc = Proc();
        Assert.False(DanfeRtcData.From(proc).ShowIcmsMonoLine);

        proc.NFe.infNFe.total.ICMSTot.vICMSMono = 7m;
        var data = DanfeRtcData.From(proc);

        Assert.True(data.ShowIcmsMonoLine);
        Assert.Equal("7,00", data.TotalIcmsMono);
    }

    [Theory]
    [InlineData(CRT.SimplesNacional, "1 - SIMPLES NACIONAL")]
    [InlineData(CRT.SimplesNacionalExcessoSublimite, "2 - SIMPLES NACIONAL, EXCESSO DE SUBLIMITE DE RECEITA BRUTA")]
    [InlineData(CRT.RegimeNormal, "3 - REGIME NORMAL")]
    [InlineData(CRT.SimplesNacionalMei, "4 - SIMPLES NACIONAL - MEI")]
    public void Crt_prints_code_and_description(CRT crt, string expected)
    {
        var proc = Proc();
        proc.NFe.infNFe.emit.CRT = crt;

        Assert.Equal(expected, DanfeRtcData.From(proc).Crt);
    }

    [Fact]
    public void Formatting_ignores_the_thread_culture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");

            Assert.Equal("50.638,50", DanfeRtcData.From(Proc()).Items[0].Bases.Split('\n')[1]);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void ApplyTo_sets_one_array_entry_per_item()
    {
        var proc = Proc();
        using var report = new FastReport.Report();

        var data = DanfeRtcData.From(proc);

        data.ApplyTo(report);

        var bases = Assert.IsType<string[]>(report.GetParameterValue("RtcItemBases"));
        Assert.Equal(proc.NFe.infNFe.det.Count, bases.Length);
        Assert.Equal(data.Items[0].Bases, bases[0]);
        Assert.Equal(data.Crt, report.GetParameterValue("RtcCrt"));
        Assert.Equal(false, report.GetParameterValue("RtcShowFcpLine"));
    }
}
