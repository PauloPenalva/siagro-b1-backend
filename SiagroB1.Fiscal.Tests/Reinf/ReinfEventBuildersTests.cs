using System.Xml;
using SiagroB1.Domain.Enums;
using SiagroB1.Fiscal.Reinf;
using SiagroB1.Fiscal.Reinf.Events;

namespace SiagroB1.Fiscal.Tests.Reinf;

public class ReinfEventBuildersTests
{
    private const string EventId = "ID1123456780000002026102410000000001";
    private static readonly ReinfHeader H = new(ReinfEnvironment.ProductionRestricted, "12345678", "1.1.290");
    private static readonly SoftwareHouse Soft = new("11222333000144", "IDX Tecnologia", "Contato IDX", "1133334444", "idx@idx.com.br");

    private static (XmlDocument Doc, XmlNamespaceManager Ns) Ns(XmlDocument d, string uri)
    {
        var ns = new XmlNamespaceManager(d.NameTable);
        ns.AddNamespace("r", uri);
        return (d, ns);
    }

    private static string? Val(XmlDocument d, XmlNamespaceManager ns, string xpath) =>
        d.SelectSingleNode(xpath, ns)?.InnerText;

    private static R2055Detail Detail(int ind = 1) =>
        new(ind, 1000m, 15m, 0.5m, 2m, null, null, 0m, 0m);

    private static R2055Input In2055(bool payroll = false, int retif = 1, string? receipt = null, R2055Detail? det = null) =>
        new(H, "2026-09", retif, receipt, "98765432000110", 2, "12345678901", payroll, [det ?? Detail()]);

    [Fact]
    public void R2055_BasicStructure()
    {
        var (d, ns) = Ns(R2055Builder.Build(In2055(), EventId), ReinfNamespaces.AquisicaoProdRural);
        Assert.Equal(EventId, ((XmlElement)d.SelectSingleNode("/r:Reinf/r:evtAqProd", ns)!).GetAttribute("id"));
        Assert.Equal("1", Val(d, ns, "//r:ideEvento/r:indRetif"));
        Assert.Null(d.SelectSingleNode("//r:nrRecibo", ns));
        Assert.Equal("2026-09", Val(d, ns, "//r:perApur"));
        Assert.Equal("2", Val(d, ns, "//r:tpAmb"));
        Assert.Equal("1", Val(d, ns, "//r:procEmi"));
        Assert.Equal("1.1.290", Val(d, ns, "//r:verProc"));
        Assert.Equal("1", Val(d, ns, "//r:ideContri/r:tpInsc"));
        Assert.Equal("12345678", Val(d, ns, "//r:ideContri/r:nrInsc"));
        Assert.Equal("1", Val(d, ns, "//r:ideEstabAdquir/r:tpInscAdq"));
        Assert.Equal("98765432000110", Val(d, ns, "//r:ideEstabAdquir/r:nrInscAdq"));
        Assert.Equal("2", Val(d, ns, "//r:ideProdutor/r:tpInscProd"));
        Assert.Equal("12345678901", Val(d, ns, "//r:ideProdutor/r:nrInscProd"));
        Assert.Equal("1000,00", Val(d, ns, "//r:detAquis/r:vlrBruto"));
        Assert.Equal("15,00", Val(d, ns, "//r:detAquis/r:vlrCPDescPR"));
        Assert.Equal("0,50", Val(d, ns, "//r:detAquis/r:vlrRatDescPR"));
        Assert.Equal("2,00", Val(d, ns, "//r:detAquis/r:vlrSenarDesc"));
        Assert.Null(d.SelectSingleNode("//r:infoProcJud", ns));
    }

    [Fact]
    public void R2055_PayrollOption_WritesIndOpcCp_BeforeDetAquis()
    {
        var (d, ns) = Ns(R2055Builder.Build(In2055(payroll: true), EventId), ReinfNamespaces.AquisicaoProdRural);
        Assert.Equal("S", Val(d, ns, "//r:ideProdutor/r:indOpcCP"));
        Assert.NotNull(d.SelectSingleNode("//r:indOpcCP/following-sibling::r:detAquis", ns));
        var (d2, ns2) = Ns(R2055Builder.Build(In2055(), EventId), ReinfNamespaces.AquisicaoProdRural);
        Assert.Null(d2.SelectSingleNode("//r:indOpcCP", ns2));
    }

    [Fact]
    public void R2055_Judicial_WritesProcessAndNotWithheld()
    {
        var det = new R2055Detail(4, 1000m, 0m, 0m, 0m, "12345678901234", "999", 15m, 0m);
        var (d, ns) = Ns(R2055Builder.Build(In2055(det: det), EventId), ReinfNamespaces.AquisicaoProdRural);
        Assert.Equal("4", Val(d, ns, "//r:detAquis/r:indAquis"));
        Assert.Equal("12345678901234", Val(d, ns, "//r:infoProcJud/r:nrProcJud"));
        Assert.Equal("999", Val(d, ns, "//r:infoProcJud/r:codSusp"));
        Assert.Equal("15,00", Val(d, ns, "//r:infoProcJud/r:vlrCPNRet"));
        Assert.Null(d.SelectSingleNode("//r:infoProcJud/r:vlrRatNRet", ns));
        Assert.Null(d.SelectSingleNode("//r:vlrSenarNRet", ns));
    }

    [Fact]
    public void R2055_Rectification_WritesReceipt()
    {
        var (d, ns) = Ns(R2055Builder.Build(In2055(retif: 2, receipt: "1-01-2026-0001-123"), EventId), ReinfNamespaces.AquisicaoProdRural);
        Assert.Equal("2", Val(d, ns, "//r:indRetif"));
        Assert.Equal("1-01-2026-0001-123", Val(d, ns, "//r:nrRecibo"));
        Assert.NotNull(d.SelectSingleNode("//r:nrRecibo/following-sibling::r:perApur", ns));
    }

    [Fact]
    public void R2055_RectificationWithoutReceipt_Throws() =>
        Assert.Throws<InvalidOperationException>(() => R2055Builder.Build(In2055(retif: 2, receipt: null), EventId));

    [Fact]
    public void R2099_FlagsFollowHasAcquisitions()
    {
        foreach (var has in new[] { true, false })
        {
            var (d, ns) = Ns(R2099Builder.Build(new R2099Input(H, "2026-09", "Fulano", "12345678901", "11999998888", "a@b.com", has), EventId), ReinfNamespaces.Fechamento);
            Assert.Equal(EventId, ((XmlElement)d.SelectSingleNode("/r:Reinf/r:evtFechaEvPer", ns)!).GetAttribute("id"));
            Assert.Equal(has ? "S" : "N", Val(d, ns, "//r:evtAquis"));
            foreach (var f in new[] { "evtServTm", "evtServPr", "evtAssDespRec", "evtAssDespRep", "evtComProd", "evtCPRB" })
                Assert.Equal("N", Val(d, ns, $"//r:infoFech/r:{f}"));
            Assert.Equal("Fulano", Val(d, ns, "//r:ideRespInf/r:nmResp"));
            Assert.Equal("12345678901", Val(d, ns, "//r:ideRespInf/r:cpfResp"));
            Assert.Equal("2026-09", Val(d, ns, "//r:perApur"));
        }
    }

    [Fact]
    public void R2098_Structure()
    {
        var (d, ns) = Ns(R2098Builder.Build(new R2098Input(H, "2026-09"), EventId), ReinfNamespaces.ReabreEvPer);
        Assert.Equal(EventId, ((XmlElement)d.SelectSingleNode("/r:Reinf/r:evtReabreEvPer", ns)!).GetAttribute("id"));
        Assert.Equal("2026-09", Val(d, ns, "//r:ideEvento/r:perApur"));
        Assert.Equal("12345678", Val(d, ns, "//r:ideContri/r:nrInsc"));
    }

    [Fact]
    public void R9000_Structure()
    {
        var (d, ns) = Ns(R9000Builder.Build(new R9000Input(H, "R-2055", "1-01-2026-0001-123", "2026-09"), EventId), ReinfNamespaces.Exclusao);
        Assert.Equal(EventId, ((XmlElement)d.SelectSingleNode("/r:Reinf/r:evtExclusao", ns)!).GetAttribute("id"));
        Assert.Equal("R-2055", Val(d, ns, "//r:infoExclusao/r:tpEvento"));
        Assert.Equal("1-01-2026-0001-123", Val(d, ns, "//r:infoExclusao/r:nrRecEvt"));
        Assert.Equal("2026-09", Val(d, ns, "//r:infoExclusao/r:perApur"));
    }

    private static R1000Input In1000(R1000Operation op) =>
        new(H, op, "2026-01", "01", true, false, false, 0, "Fulano", "12345678901", "1133334444", "f@x.com", Soft);

    [Fact]
    public void R1000_Include()
    {
        var (d, ns) = Ns(R1000Builder.Build(In1000(R1000Operation.Include), EventId), ReinfNamespaces.InfoContribuinte);
        Assert.Equal(EventId, ((XmlElement)d.SelectSingleNode("/r:Reinf/r:evtInfoContri", ns)!).GetAttribute("id"));
        Assert.Equal("1.1.290", Val(d, ns, "//r:verProc"));
        Assert.Equal("2026-01", Val(d, ns, "//r:inclusao/r:idePeriodo/r:iniValid"));
        Assert.Null(d.SelectSingleNode("//r:fimValid", ns));
        Assert.Equal("01", Val(d, ns, "//r:infoCadastro/r:classTrib"));
        Assert.Equal("1", Val(d, ns, "//r:indEscrituracao"));
        Assert.Equal("0", Val(d, ns, "//r:indDesoneracao"));
        Assert.Equal("0", Val(d, ns, "//r:indAcordoIsenMulta"));
        Assert.Equal("0", Val(d, ns, "//r:indSitPJ"));
        Assert.Equal("Fulano", Val(d, ns, "//r:contato/r:nmCtt"));
        Assert.Equal("1133334444", Val(d, ns, "//r:contato/r:foneFixo"));
        Assert.Equal("f@x.com", Val(d, ns, "//r:contato/r:email"));
        Assert.Equal("11222333000144", Val(d, ns, "//r:softHouse/r:cnpjSoftHouse"));
        Assert.Equal("IDX Tecnologia", Val(d, ns, "//r:softHouse/r:nmRazao"));
        Assert.Equal("Contato IDX", Val(d, ns, "//r:softHouse/r:nmCont"));
        Assert.NotNull(d.SelectSingleNode("//r:contato/following-sibling::r:softHouse", ns));
    }

    [Fact]
    public void R1000_UpdateAndDelete()
    {
        var (d, ns) = Ns(R1000Builder.Build(In1000(R1000Operation.Update), EventId), ReinfNamespaces.InfoContribuinte);
        Assert.NotNull(d.SelectSingleNode("//r:infoContri/r:alteracao/r:infoCadastro", ns));
        Assert.Null(d.SelectSingleNode("//r:inclusao", ns));
        Assert.Null(d.SelectSingleNode("//r:novaValidade", ns));
        var (e, ns2) = Ns(R1000Builder.Build(In1000(R1000Operation.Delete), EventId), ReinfNamespaces.InfoContribuinte);
        Assert.Equal("2026-01", Val(e, ns2, "//r:infoContri/r:exclusao/r:idePeriodo/r:iniValid"));
        Assert.Null(e.SelectSingleNode("//r:infoCadastro", ns2));
    }

    [Fact]
    public void R1000_OmitsOptionalWhenNull()
    {
        var i = In1000(R1000Operation.Include) with { CompanySituation = null, ContactPhone = null, ContactEmail = null };
        var (d, ns) = Ns(R1000Builder.Build(i, EventId), ReinfNamespaces.InfoContribuinte);
        Assert.Null(d.SelectSingleNode("//r:indSitPJ", ns));
        Assert.Null(d.SelectSingleNode("//r:contato/r:foneFixo", ns));
        Assert.Null(d.SelectSingleNode("//r:contato/r:email", ns));
    }
}
