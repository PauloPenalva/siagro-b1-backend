using System.Globalization;
using System.Text;
using System.Xml.Linq;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Services.PurchaseInvoices;

/// <summary>Item (det) da NF-e do fornecedor: identificação, quantidade e a tributação que o fornecedor destacou.</summary>
public sealed record SupplierNfeItem(
    int ItemNumber, string? ProductCode, string? ProductName, string? UnitOfMeasure, decimal Quantity, decimal UnitPrice,
    string? Cfop, string? Ncm, string? BenefitCode, byte? GoodsOrigin,
    string? CstIcms, decimal IcmsBase, decimal IcmsBaseReduction, decimal IcmsRate, decimal IcmsValue,
    decimal IcmsDeferral, decimal IcmsOperationValue, decimal IcmsDeferredValue, decimal IcmsStValue, decimal IpiValue,
    string? CstPis, decimal PisBase, decimal PisRate, decimal PisValue,
    string? CstCofins, decimal CofinsBase, decimal CofinsRate, decimal CofinsValue,
    string? IbsCbsCst, string? IbsCbsClassCode, decimal IbsCbsBase,
    decimal IbsStateRate, decimal IbsMunicipalRate, decimal IbsRateReduction, decimal IbsStateValue, decimal IbsMunicipalValue,
    decimal CbsRate, decimal CbsRateReduction, decimal CbsValue);

/// <summary>NF-e do fornecedor lida: o <c>infNFe</c> (para o cabeçalho), a chave e os itens.</summary>
public sealed record SupplierNfe(XElement InfNfe, string AccessKey, IReadOnlyList<SupplierNfeItem> Items);

/// <summary>
/// Lê a NF-e do fornecedor (<c>nfeProc</c> ou <c>NFe</c>) com <see cref="XDocument"/>, como a importação sempre leu —
/// a Zeus serve à emissão. Um leitor só para a importação, a gravação do documento de terceiro e o "Devolver"
/// (spec terceiro §6). Números no formato invariante do XML, qualquer que seja a cultura da máquina.
/// </summary>
public static class SupplierNfeXmlReader
{
    private static readonly XNamespace Nfe = "http://www.portalfiscal.inf.br/nfe";

    public static SupplierNfe Read(byte[] xmlData)
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

        var infNfe = document.Descendants(Nfe + "infNFe").FirstOrDefault()
                     ?? throw new DefaultException("XML não parece uma NF-e: elemento infNFe não encontrado.");

        // A chave vem no atributo Id como "NFe" + 44 dígitos.
        var accessKey = (infNfe.Attribute("Id")?.Value ?? string.Empty)
            .Replace("NFe", string.Empty, StringComparison.OrdinalIgnoreCase);

        return new SupplierNfe(infNfe, accessKey, infNfe.Elements(Nfe + "det").Select(ReadItem).ToList());
    }

    private static SupplierNfeItem ReadItem(XElement det)
    {
        var prod = det.Element(Nfe + "prod");
        var imposto = det.Element(Nfe + "imposto");
        // ICMS, PIS e COFINS têm um filho só, cujo nome é o grupo (ICMS51, ICMSSN101, PISOutr...).
        var icms = imposto?.Element(Nfe + "ICMS")?.Elements().FirstOrDefault();
        var pis = imposto?.Element(Nfe + "PIS")?.Elements().FirstOrDefault();
        var cofins = imposto?.Element(Nfe + "COFINS")?.Elements().FirstOrDefault();
        var ibsCbs = imposto?.Element(Nfe + "IBSCBS");
        var gIbsCbs = ibsCbs?.Element(Nfe + "gIBSCBS");
        var gIbsUf = gIbsCbs?.Element(Nfe + "gIBSUF");
        var gIbsMun = gIbsCbs?.Element(Nfe + "gIBSMun");
        var gCbs = gIbsCbs?.Element(Nfe + "gCBS");
        var ipiValue = imposto?.Element(Nfe + "IPI")?.Descendants(Nfe + "vIPI").Select(e => Parse(e.Value)).FirstOrDefault() ?? 0m;

        return new SupplierNfeItem(
            ItemNumber: int.TryParse(det.Attribute("nItem")?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0,
            ProductCode: Value(prod, "cProd"), ProductName: Value(prod, "xProd"), UnitOfMeasure: Value(prod, "uCom"),
            Quantity: Number(prod, "qCom"), UnitPrice: Number(prod, "vUnCom"),
            Cfop: Value(prod, "CFOP"), Ncm: Value(prod, "NCM"), BenefitCode: Value(prod, "cBenef"),
            GoodsOrigin: byte.TryParse(Value(icms, "orig"), NumberStyles.None, CultureInfo.InvariantCulture, out var origin) ? origin : null,
            CstIcms: Value(icms, "CST") ?? Value(icms, "CSOSN"),
            IcmsBase: Number(icms, "vBC"), IcmsBaseReduction: Number(icms, "pRedBC"), IcmsRate: Number(icms, "pICMS"),
            IcmsValue: Number(icms, "vICMS"), IcmsDeferral: Number(icms, "pDif"), IcmsOperationValue: Number(icms, "vICMSOp"),
            IcmsDeferredValue: Number(icms, "vICMSDif"), IcmsStValue: Number(icms, "vICMSST"), IpiValue: ipiValue,
            CstPis: Value(pis, "CST"), PisBase: Number(pis, "vBC"), PisRate: Number(pis, "pPIS"), PisValue: Number(pis, "vPIS"),
            CstCofins: Value(cofins, "CST"), CofinsBase: Number(cofins, "vBC"), CofinsRate: Number(cofins, "pCOFINS"),
            CofinsValue: Number(cofins, "vCOFINS"),
            IbsCbsCst: Value(ibsCbs, "CST"), IbsCbsClassCode: Value(ibsCbs, "cClassTrib"), IbsCbsBase: Number(gIbsCbs, "vBC"),
            IbsStateRate: Number(gIbsUf, "pIBSUF"), IbsMunicipalRate: Number(gIbsMun, "pIBSMun"),
            IbsRateReduction: Number(gIbsUf?.Element(Nfe + "gRed"), "pRedAliq"),
            IbsStateValue: Number(gIbsUf, "vIBSUF"), IbsMunicipalValue: Number(gIbsMun, "vIBSMun"),
            CbsRate: Number(gCbs, "pCBS"), CbsRateReduction: Number(gCbs?.Element(Nfe + "gRed"), "pRedAliq"),
            CbsValue: Number(gCbs, "vCBS"));
    }

    private static string? Value(XElement? parent, string name) => parent?.Element(Nfe + name)?.Value;

    private static decimal Number(XElement? parent, string name) => Parse(Value(parent, name));

    private static decimal Parse(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number) ? number : 0m;
}
