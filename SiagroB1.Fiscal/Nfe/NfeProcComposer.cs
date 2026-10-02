using System.Xml.Linq;

namespace SiagroB1.Fiscal.Nfe;

/// <summary>
/// <c>nfeProc</c> = NF-e assinada + <c>protNFe</c>, montado por texto para a parte assinada ficar
/// byte a byte igual à transmitida (reserializar o objeto poderia mexer em espaços e prefixos).
/// </summary>
public static class NfeProcComposer
{
    public static string Compose(string signedNfeXml, string protocolXml) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
        "<nfeProc versao=\"4.00\" xmlns=\"http://www.portalfiscal.inf.br/nfe\">" +
        Body(signedNfeXml) +
        Body(protocolXml) +
        "</nfeProc>";

    /// <summary>Texto do primeiro <c>DigestValue</c> do XML assinado (ignora namespaces).</summary>
    public static string? SignedDigest(string signedNfeXml) => FirstValue(signedNfeXml, "DigestValue");

    /// <summary>Texto do <c>digVal</c> do protNFe — o digest do XML que a SEFAZ autorizou.</summary>
    public static string? ProtocolDigest(string protocolXml) => FirstValue(protocolXml, "digVal");

    private static string? FirstValue(string xml, string localName) =>
        XDocument.Parse(xml).Descendants().FirstOrDefault(e => e.Name.LocalName == localName)?.Value.Trim();

    private static string Body(string xml)
    {
        var text = xml.Trim();

        if (text.StartsWith("<?xml", StringComparison.Ordinal))
            text = text[(text.IndexOf("?>", StringComparison.Ordinal) + 2)..].Trim();

        return text;
    }
}
