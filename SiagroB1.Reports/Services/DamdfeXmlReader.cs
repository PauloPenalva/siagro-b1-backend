using System.Globalization;
using System.Xml.Linq;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Reports.Dtos;

namespace SiagroB1.Reports.Services;

/// <summary>Lê o <c>mdfeProc</c> (MDF-e 3.00 + protMDFe, modal rodoviário) e monta o <see cref="DamdfeDto"/> em pt-BR.</summary>
public static class DamdfeXmlReader
{
    private static readonly XNamespace Ns = "http://www.portalfiscal.inf.br/mdfe";
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static DamdfeDto Read(string xml, bool cancelled, bool closed)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(xml);
        }
        catch (System.Xml.XmlException)
        {
            throw new DefaultException("O XML autorizado do MDF-e está ilegível.");
        }

        var inf = document.Descendants(Ns + "infMDFe").FirstOrDefault()
                  ?? throw new DefaultException("O XML gravado não é de um MDF-e.");
        var prot = document.Descendants(Ns + "infProt").FirstOrDefault();
        var ide = inf.Element(Ns + "ide");
        var emit = inf.Element(Ns + "emit");
        var rodo = inf.Descendants(Ns + "rodo").FirstOrDefault();
        var antt = rodo?.Element(Ns + "infANTT");
        var tot = inf.Element(Ns + "tot");
        var key = (string?)inf.Attribute("Id") is { Length: > 4 } id ? id[4..] : prot?.V("chMDFe") ?? "";

        var dto = new DamdfeDto
        {
            AccessKey = key,
            FormattedAccessKey = DacteXmlReader.FormatKey(key),
            Protocol = prot is null ? "" : $"{prot.V("nProt")}  {Date(prot.V("dhRecbto"))}".Trim(),
            QrCode = document.Descendants(Ns + "qrCodMDFe").FirstOrDefault()?.Value.Trim() ?? "",
            Homologation = ide?.V("tpAmb") == "2",
            Cancelled = cancelled,
            Closed = closed,
            IssuerName = emit?.V("xNome") ?? "",
            IssuerDetails = IssuerDetails(emit),
            Modal = ide?.V("modal") switch { "1" => "RODOVIÁRIO", "2" => "AÉREO", "3" => "AQUAVIÁRIO", "4" => "FERROVIÁRIO", var o => o ?? "" },
            Series = ide?.V("serie") ?? "",
            Number = int.TryParse(ide?.V("nMDF"), out var n) ? n.ToString("000\\.000\\.000", CultureInfo.InvariantCulture) : ide?.V("nMDF") ?? "",
            IssuedAt = Date(ide?.V("dhEmi")),
            UfStart = ide?.V("UFIni") ?? "",
            UfEnd = ide?.V("UFFim") ?? "",
            Route = string.Join(" > ", ide?.Elements(Ns + "infPercurso").Select(p => p.V("UFPer")) ?? []),
            LoadingCities = string.Join(", ", ide?.Elements(Ns + "infMunCarrega").Select(c => c.V("xMunCarrega")) ?? []),
            CteCount = tot?.V("qCTe") ?? "0",
            NfeCount = tot?.V("qNFe") ?? "0",
            CargoValue = Number(tot?.V("vCarga"), "N2"),
            WeightUnit = tot?.V("cUnid") switch { "01" => "KG", "02" => "TON", var o => o ?? "" },
            GrossWeight = Number(tot?.V("qCarga"), "N4"),
            Rntrc = antt?.V("RNTRC") ?? "",
        };

        var prod = inf.Element(Ns + "prodPred");
        dto.Product = prod is null ? "" : JoinNonEmpty("  ", prod.V("xProd"), Prefix("NCM: ", prod.V("NCM")));

        var vehicles = new List<string>();
        if (rodo?.Element(Ns + "veicTracao") is { } traction)
            vehicles.Add(Vehicle(traction, "TRAÇÃO"));
        vehicles.AddRange(rodo?.Elements(Ns + "veicReboque").Select(r => Vehicle(r, "REBOQUE")) ?? []);
        dto.Vehicles = string.Join("\n", vehicles);
        dto.Drivers = string.Join("\n", rodo?.Element(Ns + "veicTracao")?.Elements(Ns + "condutor")
            .Select(c => $"{TaxId(c.V("CPF"))}  {c.V("xNome")}") ?? []);

        dto.Ciot = string.Join("\n", antt?.Elements(Ns + "infCIOT")
            .Select(c => $"{c.V("CIOT")}  RESP.: {TaxId(c.V("CNPJ") ?? c.V("CPF"))}") ?? []);
        dto.TollVouchers = string.Join("\n", antt?.Element(Ns + "valePed")?.Elements(Ns + "disp")
            .Select(d => JoinNonEmpty("  ", $"FORN.: {TaxId(d.V("CNPJForn"))}", Prefix("RESP.: ", TaxId(d.V("CNPJPg") ?? d.V("CPFPg"))),
                $"Nº COMPROVANTE: {d.V("nCompra")}", Prefix("VALOR: ", Number(d.V("vValePed"), "N2")))) ?? []);

        var docs = new List<string>();
        foreach (var city in inf.Element(Ns + "infDoc")?.Elements(Ns + "infMunDescarga") ?? [])
        {
            docs.Add($"DESCARGA: {city.V("xMunDescarga")} ({city.V("cMunDescarga")})");
            docs.AddRange(city.Elements(Ns + "infCTe").Select(c => $"     CT-e  {DacteXmlReader.FormatKey(c.V("chCTe") ?? "")}"));
            docs.AddRange(city.Elements(Ns + "infNFe").Select(c => $"     NF-e  {DacteXmlReader.FormatKey(c.V("chNFe") ?? "")}"));
        }
        dto.Documents = string.Join("\n", docs);

        var adic = inf.Element(Ns + "infAdic");
        dto.Remarks = JoinNonEmpty("\n", adic?.V("infCpl"), adic?.V("infAdFisco"));
        return dto;
    }

    private static string? V(this XElement e, string name) => e.Element(Ns + name)?.Value.Trim();

    private static string Vehicle(XElement v, string kind) =>
        JoinNonEmpty("   ", kind, v.V("placa"), v.V("UF"), Prefix("RENAVAM: ", v.V("RENAVAM")),
            Prefix("RNTRC: ", v.Element(Ns + "prop")?.V("RNTRC")));

    private static string IssuerDetails(XElement? emit)
    {
        if (emit is null)
            return "";
        var end = emit.Element(Ns + "enderEmit");
        return string.Join("\n", new[]
        {
            JoinNonEmpty(", ", end?.V("xLgr"), end?.V("nro"), end?.V("xCpl")),
            JoinNonEmpty(" - ", end?.V("xBairro"), JoinNonEmpty(" - ", end?.V("xMun"), end?.V("UF")), Cep(end?.V("CEP"))),
            JoinNonEmpty("   ", $"CNPJ/CPF: {TaxId(emit.V("CNPJ") ?? emit.V("CPF"))}", Prefix("IE: ", emit.V("IE")), Prefix("FONE: ", end?.V("fone"))),
        }.Where(s => s.Length > 0));
    }

    private static string JoinNonEmpty(string separator, params string?[] parts) =>
        string.Join(separator, parts.Where(p => !string.IsNullOrWhiteSpace(p)));

    private static string? Prefix(string prefix, string? value) => string.IsNullOrWhiteSpace(value) ? null : prefix + value;

    private static string? Cep(string? cep) => cep is { Length: 8 } ? $"{cep[..5]}-{cep[5..]}" : cep;

    private static string? TaxId(string? id) => id switch
    {
        { Length: 14 } => $"{id[..2]}.{id[2..5]}.{id[5..8]}/{id[8..12]}-{id[12..]}",
        { Length: 11 } => $"{id[..3]}.{id[3..6]}.{id[6..9]}-{id[9..]}",
        _ => id,
    };

    private static string Number(string? value, string format) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d.ToString(format, PtBr) : "";

    private static string Date(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.ToString("dd/MM/yyyy HH:mm:ss", PtBr)
            : value ?? "";
}
