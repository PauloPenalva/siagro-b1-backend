using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;

namespace SiagroB1.Fiscal.Reinf;

/// <summary>SignedXml que resolve a referência pelo atributo "id" minúsculo dos eventos da Reinf.</summary>
public class ReinfSignedXml(XmlDocument document) : SignedXml(document)
{
    public override XmlElement? GetIdElement(XmlDocument? document, string idValue) =>
        base.GetIdElement(document, idValue)
        ?? document?.SelectSingleNode($"//*[@id='{idValue}']") as XmlElement;
}

/// <summary>Assina o evento da EFD-Reinf (RSA-SHA256, enveloped + C14N), assinatura como último filho de Reinf.</summary>
public static class ReinfSigner
{
    private const string C14N = "http://www.w3.org/TR/2001/REC-xml-c14n-20010315";

    public static XmlDocument Sign(XmlDocument eventDoc, X509Certificate2 cert)
    {
        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.LoadXml(eventDoc.OuterXml);

        var root = doc.DocumentElement ?? throw new ArgumentException("Documento vazio.", nameof(eventDoc));
        var evt = root.ChildNodes.OfType<XmlElement>().FirstOrDefault()
                  ?? throw new ArgumentException("Evento sem conteúdo.", nameof(eventDoc));
        var id = evt.GetAttribute("id");
        if (string.IsNullOrEmpty(id)) throw new ArgumentException("Evento sem atributo id.", nameof(eventDoc));

        using var key = cert.GetRSAPrivateKey() ?? throw new ArgumentException("Certificado sem chave privada.", nameof(cert));

        var signed = new ReinfSignedXml(doc) { SigningKey = key };
        signed.SignedInfo!.SignatureMethod = SignedXml.XmlDsigRSASHA256Url;
        signed.SignedInfo.CanonicalizationMethod = C14N;

        var reference = new Reference("#" + id) { DigestMethod = SignedXml.XmlDsigSHA256Url };
        reference.AddTransform(new XmlDsigEnvelopedSignatureTransform());
        reference.AddTransform(new XmlDsigC14NTransform());
        signed.AddReference(reference);

        var keyInfo = new KeyInfo();
        keyInfo.AddClause(new KeyInfoX509Data(cert));
        signed.KeyInfo = keyInfo;

        signed.ComputeSignature();
        root.AppendChild(doc.ImportNode(signed.GetXml(), true));
        return doc;
    }
}
