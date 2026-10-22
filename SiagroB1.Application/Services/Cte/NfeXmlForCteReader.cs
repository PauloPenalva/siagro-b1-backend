using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Services.Cte;

public sealed record NfeForCteParty(string TaxId, string Name, string? MunicipalityCode);

/// <summary>O que o CT-e aproveita da NF-e transportada.</summary>
public sealed record NfeForCte(
    string AccessKey, NfeForCteParty Emitter, NfeForCteParty? Recipient, decimal TotalValue,
    string? PredominantProduct, decimal GrossWeight);

/// <summary>Leitura do XML da NF-e (nfeProc ou NFe, modelo 55) para preencher o CT-e.</summary>
public static class NfeXmlForCteReader
{
    public const string NotNfeMessage = "O arquivo não é o XML de uma NF-e (modelo 55).";

    private static readonly XNamespace Ns = "http://www.portalfiscal.inf.br/nfe";

    public static NfeForCte Read(string xml)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(xml);
        }
        catch (XmlException)
        {
            throw new DefaultException(NotNfeMessage);
        }

        var root = document.Root;
        var nfe = root?.Name == Ns + "nfeProc" ? root.Element(Ns + "NFe") : root?.Name == Ns + "NFe" ? root : null;
        var inf = nfe?.Element(Ns + "infNFe");
        var ide = inf?.Element(Ns + "ide");
        if (inf is null || ide is null || (string?)ide.Element(Ns + "mod") != "55")
            throw new DefaultException(NotNfeMessage);

        var key = ((string?)inf.Attribute("Id") ?? string.Empty).Replace("NFe", string.Empty).Trim();
        if (key.Length != 44)
            key = (string?)root!.Element(Ns + "protNFe")?.Element(Ns + "infProt")?.Element(Ns + "chNFe") ?? key;

        var emitter = Party(inf.Element(Ns + "emit"), "enderEmit") ?? throw new DefaultException(NotNfeMessage);
        var recipient = Party(inf.Element(Ns + "dest"), "enderDest");

        var total = Decimal(inf.Element(Ns + "total")?.Element(Ns + "ICMSTot")?.Element(Ns + "vNF"));
        var product = inf.Elements(Ns + "det")
            .Select(d => d.Element(Ns + "prod"))
            .Where(p => p is not null)
            .OrderByDescending(p => Decimal(p!.Element(Ns + "vProd")))
            .Select(p => ((string?)p!.Element(Ns + "xProd"))?.Trim())
            .FirstOrDefault();
        var weight = inf.Element(Ns + "transp")?.Elements(Ns + "vol").Sum(v => Decimal(v.Element(Ns + "pesoB"))) ?? 0m;

        return new NfeForCte(key, emitter, recipient, total, product, weight);
    }

    private static NfeForCteParty? Party(XElement? element, string addressName)
    {
        if (element is null) return null;
        var taxId = ((string?)element.Element(Ns + "CNPJ") ?? (string?)element.Element(Ns + "CPF"))?.Trim();
        if (string.IsNullOrEmpty(taxId)) return null;
        return new NfeForCteParty(taxId, ((string?)element.Element(Ns + "xNome"))?.Trim() ?? string.Empty,
            ((string?)element.Element(Ns + addressName)?.Element(Ns + "cMun"))?.Trim());
    }

    private static decimal Decimal(XElement? element) =>
        element is not null && decimal.TryParse(element.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : 0m;
}
