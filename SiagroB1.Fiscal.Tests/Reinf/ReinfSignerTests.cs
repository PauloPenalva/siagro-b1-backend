using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;
using SiagroB1.Domain.Enums;
using SiagroB1.Fiscal.Reinf;
using SiagroB1.Fiscal.Reinf.Events;
using SiagroB1.Fiscal.Tests.Support;

namespace SiagroB1.Fiscal.Tests.Reinf;

public class ReinfSignerTests
{
    internal const string EventId = "ID1123456780000002026102410000000001";
    internal static readonly ReinfHeader H = new(ReinfEnvironment.ProductionRestricted, "12345678", "1.1.290");
    private static readonly SoftwareHouse Soft = new("11222333000144", "IDX Tecnologia", "Contato IDX", "1133334444", "idx@idx.com.br");

    internal static X509Certificate2 Cert() =>
        X509CertificateLoader.LoadPkcs12(TestCertificates.CreatePfx(), TestCertificates.Password, X509KeyStorageFlags.Exportable);

    internal static XmlDocument R2099(string id = EventId) =>
        R2099Builder.Build(new R2099Input(H, "2026-09", "Fulano", "12345678901", "11999998888", "a@b.com", true), id);

    internal static bool Check(XmlDocument signed, X509Certificate2 cert)
    {
        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.LoadXml(signed.OuterXml);
        var sx = new ReinfSignedXml(doc);
        sx.LoadXml((XmlElement)doc.GetElementsByTagName("Signature", SignedXml.XmlDsigNamespaceUrl)[0]!);
        return sx.CheckSignature(cert, true);
    }

    [Fact]
    public void Sign_R2099_IsValidAndPassesXsd()
    {
        using var cert = Cert();
        var signed = ReinfSigner.Sign(R2099(), cert);
        Assert.True(Check(signed, cert));
        Assert.Empty(new ReinfSchemaValidator().Validate(signed));
        var last = signed.DocumentElement!.ChildNodes.OfType<XmlElement>().Last();
        Assert.Equal("Signature", last.LocalName);
        Assert.Contains("#" + EventId, signed.OuterXml);
    }

    [Fact]
    public void Sign_TamperedValue_FailsCheck()
    {
        using var cert = Cert();
        var signed = ReinfSigner.Sign(R2099(), cert);
        var ns = new XmlNamespaceManager(signed.NameTable);
        ns.AddNamespace("r", ReinfNamespaces.Fechamento);
        signed.SelectSingleNode("//r:nmResp", ns)!.InnerText = "Outro";
        Assert.False(Check(signed, cert));
    }

    public static IEnumerable<object[]> AllEvents()
    {
        yield return [R1000Builder.Build(new R1000Input(H, R1000Operation.Include, "2026-01", "01", true, false, false, 0,
            "Fulano", "12345678901", "1133334444", "f@x.com", Soft), EventId)];
        yield return [R2055Builder.Build(new R2055Input(H, "2026-09", 1, null, "98765432000110", 2, "12345678901", true,
            [new R2055Detail(1, 1000m, 15m, 0.5m, 2m, null, null, 0m, 0m),
             new R2055Detail(4, 1000m, 0m, 0m, 0m, "12345678901234", "999", 15m, 0m)]), EventId)];
        yield return [R2098Builder.Build(new R2098Input(H, "2026-09"), EventId)];
        yield return [R2099()];
        yield return [R9000Builder.Build(new R9000Input(H, "R-2055", "1-01-2026-0001-123", "2026-09"), EventId)];
    }

    [Theory]
    [MemberData(nameof(AllEvents))]
    public void EveryEvent_SignedPassesXsd(XmlDocument doc)
    {
        using var cert = Cert();
        var signed = ReinfSigner.Sign(doc, cert);
        Assert.Empty(new ReinfSchemaValidator().Validate(signed));
        Assert.True(Check(signed, cert));
    }
}
