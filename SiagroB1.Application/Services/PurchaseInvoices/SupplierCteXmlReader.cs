using System.Globalization;
using System.Text;
using System.Xml.Linq;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Services.PurchaseInvoices;

/// <summary>CT-e do fornecedor de frete lido: cabeçalho, valores, ICMS destacado e as chaves das NF-e transportadas.</summary>
public sealed record SupplierCte(
    string AccessKey, string? Number, string? Series, DateTime? IssueDate, string? CteType,
    string? StartState, string? EndState, string? StartCity, string? EndCity,
    string? IssuerTaxId, string? IssuerName, string? PayerTaxId,
    decimal ServiceTotal, decimal AmountReceivable, string? IcmsCst, decimal IcmsValue,
    string? Observations, IReadOnlyList<string> TransportedKeys);

/// <summary>
/// Lê o CT-e 4.00 (<c>cteProc</c> ou <c>CTe</c>) com <see cref="XDocument"/>, no mesmo estilo do
/// <see cref="SupplierNfeXmlReader"/>. Números no formato invariante do XML, qualquer que seja a cultura da máquina.
/// </summary>
public static class SupplierCteXmlReader
{
    private static readonly XNamespace Cte = "http://www.portalfiscal.inf.br/cte";

    /// <summary>Verdadeiro se o XML tem um <c>infCte</c> no namespace do CT-e; XML inválido ou vazio dá falso.</summary>
    public static bool IsCte(byte[] xmlData)
    {
        if (xmlData is null || xmlData.Length == 0) return false;

        try
        {
            return XDocument.Parse(Encoding.UTF8.GetString(xmlData)).Descendants(Cte + "infCte").Any();
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static SupplierCte Read(byte[] xmlData)
    {
        if (xmlData is null || xmlData.Length == 0)
            throw new DefaultException("Arquivo XML vazio.");

        XDocument document;

        try
        {
            document = XDocument.Parse(Encoding.UTF8.GetString(xmlData));
        }
        catch (Exception)
        {
            throw new DefaultException("Arquivo não é um XML válido.");
        }

        var infCte = document.Descendants(Cte + "infCte").FirstOrDefault()
                     ?? throw new DefaultException("XML não parece um CT-e: elemento infCte não encontrado.");

        // A chave vem no atributo Id como "CTe" + 44 dígitos.
        var accessKey = (infCte.Attribute("Id")?.Value ?? string.Empty)
            .Replace("CTe", string.Empty, StringComparison.OrdinalIgnoreCase);

        var ide = infCte.Element(Cte + "ide");
        var emit = infCte.Element(Cte + "emit");
        var vPrest = infCte.Element(Cte + "vPrest");
        // imp/ICMS tem um filho só, cujo nome é o grupo (ICMS00, ICMS60, ICMSSN...).
        var icms = infCte.Element(Cte + "imp")?.Element(Cte + "ICMS")?.Elements().FirstOrDefault();
        var icmsCst = icms is null ? null : icms.Name.LocalName == "ICMSSN" ? "SN" : Value(icms, "CST");

        var keys = infCte.Element(Cte + "infCTeNorm")?.Element(Cte + "infDoc")?.Elements(Cte + "infNFe")
            .Select(n => Value(n, "chave"))
            .Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k!.Trim()).ToList() ?? [];

        return new SupplierCte(
            accessKey, Value(ide, "nCT"), Value(ide, "serie"), ParseDate(Value(ide, "dhEmi")), Value(ide, "tpCTe"),
            Value(ide, "UFIni"), Value(ide, "UFFim"), Value(ide, "xMunIni"), Value(ide, "xMunFim"),
            TaxId(emit), Value(emit, "xNome"), PayerTaxId(infCte, ide),
            Number(vPrest, "vTPrest"), Number(vPrest, "vRec"), icmsCst, Number(icms, "vICMS"),
            Value(infCte.Element(Cte + "compl"), "xObs"), keys);
    }

    /// <summary>Tomador: toma3/toma (toma03 nas versões antigas) aponta rem/exped/receb/dest; toma4 traz o próprio CNPJ/CPF.</summary>
    private static string? PayerTaxId(XElement infCte, XElement? ide)
    {
        var toma4 = ide?.Element(Cte + "toma4");
        if (toma4 is not null) return TaxId(toma4);

        var toma3 = ide?.Element(Cte + "toma3") ?? ide?.Element(Cte + "toma03");
        var group = Value(toma3, "toma")?.Trim() switch
        {
            "0" => "rem",
            "1" => "exped",
            "2" => "receb",
            "3" => "dest",
            _ => null,
        };

        return group is null ? null : TaxId(infCte.Element(Cte + group));
    }

    private static string? TaxId(XElement? parent) => Value(parent, "CNPJ") ?? Value(parent, "CPF");

    private static DateTime? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date.DateTime : null;

    private static string? Value(XElement? parent, string name) => parent?.Element(Cte + name)?.Value;

    private static decimal Number(XElement? parent, string name) =>
        decimal.TryParse(Value(parent, name), NumberStyles.Number, CultureInfo.InvariantCulture, out var number) ? number : 0m;
}
