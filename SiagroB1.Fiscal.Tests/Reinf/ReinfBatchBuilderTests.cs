using System.Xml;
using SiagroB1.Fiscal.Reinf;

namespace SiagroB1.Fiscal.Tests.Reinf;

public class ReinfBatchBuilderTests
{
    [Fact]
    public void Build_TwoEvents_ValidatesAgainstEnvelopeXsd()
    {
        using var cert = ReinfSignerTests.Cert();
        const string id2 = "ID1123456780000002026102410000000002";
        var events = new List<(string, XmlDocument)>
        {
            (ReinfSignerTests.EventId, ReinfSigner.Sign(ReinfSignerTests.R2099(), cert)),
            (id2, ReinfSigner.Sign(ReinfSignerTests.R2099(id2), cert)),
        };
        var xml = ReinfBatchBuilder.Build("12345678", events);
        Assert.False(xml.StartsWith('\uFEFF'));
        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.LoadXml(xml);
        Assert.Empty(new ReinfSchemaValidator().Validate(doc));
        var ns = new XmlNamespaceManager(doc.NameTable);
        ns.AddNamespace("l", ReinfNamespaces.EnvioLote);
        ns.AddNamespace("e", ReinfNamespaces.Fechamento);
        Assert.Equal("1", doc.SelectSingleNode("//l:ideContribuinte/l:tpInsc", ns)!.InnerText);
        Assert.Equal("12345678", doc.SelectSingleNode("//l:ideContribuinte/l:nrInsc", ns)!.InnerText);
        var evs = doc.SelectNodes("//l:eventos/l:evento", ns)!;
        Assert.Equal(2, evs.Count);
        Assert.Equal(id2, ((XmlElement)evs[1]!).GetAttribute("Id"));
        Assert.NotNull(evs[0]!.SelectSingleNode("e:Reinf/e:evtFechaEvPer", ns));
        // a assinatura continua válida dentro do lote
        foreach (XmlNode ev in evs)
        {
            var inner = new XmlDocument { PreserveWhitespace = true };
            inner.LoadXml(ev.SelectSingleNode("e:Reinf", ns)!.OuterXml);
            Assert.Empty(new ReinfSchemaValidator().Validate(inner));
            Assert.True(ReinfSignerTests.Check(inner, cert));
        }
    }

    [Fact]
    public void Build_51Events_Throws()
    {
        var doc = ReinfSignerTests.R2099();
        var events = Enumerable.Range(0, 51).Select(i => ($"ID{i}", doc)).ToList();
        Assert.Throws<ArgumentException>(() => ReinfBatchBuilder.Build("12345678", events));
    }

    [Fact]
    public void Build_50Events_Succeeds()
    {
        var doc = ReinfSignerTests.R2099();
        var events = Enumerable.Range(0, 50).Select(i => ($"ID{i}", doc)).ToList();
        var x = new XmlDocument();
        x.LoadXml(ReinfBatchBuilder.Build("12345678", events));
        Assert.Equal(50, x.GetElementsByTagName("evento", ReinfNamespaces.EnvioLote).Count);
    }
}
