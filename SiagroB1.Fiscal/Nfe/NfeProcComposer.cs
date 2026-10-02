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

    private static string Body(string xml)
    {
        var text = xml.Trim();

        if (text.StartsWith("<?xml", StringComparison.Ordinal))
            text = text[(text.IndexOf("?>", StringComparison.Ordinal) + 2)..].Trim();

        return text;
    }
}
