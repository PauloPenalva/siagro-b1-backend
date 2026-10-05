using DFe.Classes.Flags;
using DFe.Utils;
using NFe.Classes;
using NFe.Classes.Protocolo;
using SiagroB1.Domain.Enums;
using SiagroB1.Fiscal.Certificates;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Fiscal.Tests.Support;

namespace SiagroB1.Fiscal.Tests.Nfe;

/// <summary>
/// O procNFe é montado por texto: o XML assinado gravado entra intacto, e o protNFe da SEFAZ ao
/// lado. É o que permite montar o autorizado também na "Consultar situação".
/// </summary>
public class NfeProcComposerTests
{
    [Fact]
    public void Composed_proc_keeps_the_signed_part_and_parses_back()
    {
        using var certificate = CertificateLoader.Load(TestCertificates.CreatePfx(), TestCertificates.Password);
        var settings = new NfeServiceSettings(
            NfeEnvironment.Homologation, "SP", certificate, NfeServiceSettings.DefaultSchemasDirectory);
        var signed = NfeSigner.BuildSignAndValidate(NfeTestData.Input(), settings);
        var protocol = new protNFe
        {
            versao = "4.00",
            infProt = new infProt
            {
                tpAmb = TipoAmbiente.Homologacao, Id = "ID135260000000001", chNFe = signed.AccessKey, nProt = "135260000000001", cStat = 100,
                xMotivo = "Autorizado o uso da NF-e", dhRecbto = NfeTestData.IssuedAt, digVal = "abc=", verAplic = "SP_NFE",
            },
        };

        var proc = NfeProcComposer.Compose(signed.Xml, FuncoesXml.ClasseParaXmlString(protocol));

        var signedBody = signed.Xml[(signed.Xml.IndexOf("<NFe", StringComparison.Ordinal))..];
        Assert.Contains(signedBody.Trim(), proc);
        Assert.StartsWith("<?xml", proc);

        var parsed = new nfeProc().CarregarDeXmlString(proc);
        Assert.Equal("135260000000001", parsed.protNFe.infProt.nProt);
        Assert.Equal($"NFe{signed.AccessKey}", parsed.NFe.infNFe.Id);
    }

    [Fact]
    public void Digests_are_read_from_the_signed_xml_and_the_protocol()
    {
        using var certificate = CertificateLoader.Load(TestCertificates.CreatePfx(), TestCertificates.Password);
        var settings = new NfeServiceSettings(
            NfeEnvironment.Homologation, "SP", certificate, NfeServiceSettings.DefaultSchemasDirectory);
        var signed = NfeSigner.BuildSignAndValidate(NfeTestData.Input(), settings);
        var protocol = new protNFe
        {
            versao = "4.00",
            infProt = new infProt
            {
                tpAmb = TipoAmbiente.Homologacao, Id = "ID135260000000001", chNFe = signed.AccessKey, nProt = "135260000000001", cStat = 100,
                xMotivo = "Autorizado o uso da NF-e", dhRecbto = NfeTestData.IssuedAt,
                digVal = NfeProcComposer.SignedDigest(signed.Xml), verAplic = "SP_NFE",
            },
        };

        var signedDigest = NfeProcComposer.SignedDigest(signed.Xml);
        var protocolDigest = NfeProcComposer.ProtocolDigest(FuncoesXml.ClasseParaXmlString(protocol));

        Assert.False(string.IsNullOrWhiteSpace(signedDigest));
        Assert.Equal(signedDigest, protocolDigest);
    }
}
