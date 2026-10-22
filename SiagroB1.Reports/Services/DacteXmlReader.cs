using System.Globalization;
using System.Text;
using System.Xml.Linq;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Reports.Dtos;

namespace SiagroB1.Reports.Services;

/// <summary>Lê o <c>cteProc</c> (CT-e 4.00 + protCTe) e monta o <see cref="DacteDto"/> já formatado em pt-BR.</summary>
public static class DacteXmlReader
{
    private static readonly XNamespace Ns = "http://www.portalfiscal.inf.br/cte";
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static DacteDto Read(string xml, bool cancelled)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(xml);
        }
        catch (System.Xml.XmlException)
        {
            throw new DefaultException("O XML autorizado do CT-e está ilegível.");
        }

        var inf = document.Descendants(Ns + "infCte").FirstOrDefault()
                  ?? throw new DefaultException("O XML gravado não é de um CT-e.");
        var prot = document.Descendants(Ns + "infProt").FirstOrDefault();
        var ide = inf.Element(Ns + "ide");
        var emit = inf.Element(Ns + "emit");
        var key = (string?)inf.Attribute("Id") is { Length: > 3 } id ? id[3..] : prot?.V("chCTe") ?? "";

        var dto = new DacteDto
        {
            AccessKey = key,
            FormattedAccessKey = FormatKey(key),
            Protocol = prot is null ? "" : $"{prot.V("nProt")}  {Date(prot.V("dhRecbto"))}".Trim(),
            QrCode = document.Descendants(Ns + "qrCodCTe").FirstOrDefault()?.Value.Trim() ?? "",
            Homologation = ide?.V("tpAmb") == "2",
            Cancelled = cancelled,
            IssuerName = emit?.V("xNome") ?? "",
            IssuerDetails = IssuerDetails(emit),
            Series = ide?.V("serie") ?? "",
            Number = int.TryParse(ide?.V("nCT"), out var n) ? n.ToString("000\\.000\\.000", CultureInfo.InvariantCulture) : ide?.V("nCT") ?? "",
            IssuedAt = Date(ide?.V("dhEmi")),
            Modal = ide?.V("modal") switch
            {
                "01" => "RODOVIÁRIO", "02" => "AÉREO", "03" => "AQUAVIÁRIO", "04" => "FERROVIÁRIO", "05" => "DUTOVIÁRIO",
                "06" => "MULTIMODAL", var other => other ?? "",
            },
            CteType = ide?.V("tpCTe") switch
            {
                "0" => "NORMAL", "1" => "COMPLEMENTAR", "3" => "SUBSTITUTO", var other => other ?? "",
            },
            ServiceType = ide?.V("tpServ") switch
            {
                "0" => "NORMAL", "1" => "SUBCONTRATAÇÃO", "2" => "REDESPACHO", "3" => "REDESP. INTERMEDIÁRIO",
                "4" => "SERV. VINCULADO A MULTIMODAL", var other => other ?? "",
            },
            Cfop = ide?.V("CFOP") ?? "",
            OperationNature = ide?.V("natOp") ?? "",
            Start = $"{ide?.V("xMunIni")} - {ide?.V("UFIni")}",
            End = $"{ide?.V("xMunFim")} - {ide?.V("UFFim")}",
            Sender = Party(inf.Element(Ns + "rem"), "enderReme"),
            Recipient = Party(inf.Element(Ns + "dest"), "enderDest"),
            Shipper = Party(inf.Element(Ns + "exped"), "enderExped"),
            Receiver = Party(inf.Element(Ns + "receb"), "enderReceb"),
        };

        var toma3 = ide?.Element(Ns + "toma3")?.V("toma");
        var toma4 = ide?.Element(Ns + "toma4");
        (dto.Taker, dto.TakerParty) = toma4 is not null
            ? ("OUTROS", Party(toma4, "enderToma"))
            : toma3 switch
            {
                "0" => ("REMETENTE", dto.Sender),
                "1" => ("EXPEDIDOR", dto.Shipper),
                "2" => ("RECEBEDOR", dto.Receiver),
                "3" => ("DESTINATÁRIO", dto.Recipient),
                _ => ("", ""),
            };

        var carga = inf.Descendants(Ns + "infCarga").FirstOrDefault();
        dto.PredominantProduct = carga?.V("proPred") ?? "";
        dto.OtherCharacteristics = carga?.V("xOutCat") ?? "";
        dto.CargoValue = Money(carga?.V("vCarga"));
        dto.Quantities = string.Join("\n", carga?.Elements(Ns + "infQ").Select(q =>
            $"{q.V("tpMed")}: {Quantity(q.V("qCarga"))} {Unit(q.V("cUnid"))}") ?? []);

        var prest = inf.Element(Ns + "vPrest");
        dto.Components = string.Join("\n", prest?.Elements(Ns + "Comp").Select(c => $"{c.V("xNome")}: {Money(c.V("vComp"))}") ?? []);
        dto.ServiceTotal = Money(prest?.V("vTPrest"));
        dto.AmountToReceive = Money(prest?.V("vRec"));

        var icms = inf.Element(Ns + "imp")?.Element(Ns + "ICMS")?.Elements().FirstOrDefault();
        var cst = icms?.V("CST") ?? "";
        dto.IcmsSituation = cst switch
        {
            "00" => "00 - TRIBUTAÇÃO NORMAL", "20" => "20 - COM REDUÇÃO DE BC", "40" => "40 - ISENTA", "41" => "41 - NÃO TRIBUTADA",
            "51" => "51 - DIFERIDA", "60" => "60 - COBRADO POR ST", "90" when icms?.Name.LocalName == "ICMSOutraUF" => "90 - ICMS OUTRA UF",
            "90" => "90 - OUTROS", _ => icms?.Name.LocalName == "ICMSSN" ? "SIMPLES NACIONAL" : cst,
        };
        dto.IcmsBase = Money(icms?.V("vBC") ?? icms?.V("vBCOutraUF"));
        dto.IcmsRate = Money(icms?.V("pICMS") ?? icms?.V("pICMSOutraUF"));
        dto.IcmsValue = Money(icms?.V("vICMS") ?? icms?.V("vICMSOutraUF"));
        dto.IcmsReduction = Money(icms?.V("pRedBC") ?? icms?.V("pRedBCOutraUF"));

        var docs = inf.Descendants(Ns + "infDoc").FirstOrDefault();
        var lines = new List<string>();
        if (docs is not null)
        {
            lines.AddRange(docs.Elements(Ns + "infNFe").Select(x => $"NF-e  {FormatKey(x.V("chave") ?? "")}"));
            lines.AddRange(docs.Elements(Ns + "infNF").Select(x => $"NF  série {x.V("serie")}  nº {x.V("nDoc")}  {Money(x.V("vNF"))}"));
            lines.AddRange(docs.Elements(Ns + "infOutros").Select(x => $"OUTROS  {x.V("descOutros")} {x.V("nDoc")}".TrimEnd()));
        }
        dto.Documents = string.Join("\n", lines);

        var compl = inf.Element(Ns + "compl");
        var remarks = new List<string>();
        if (compl?.V("xObs") is { Length: > 0 } obs)
            remarks.Add(obs);
        remarks.AddRange(compl?.Elements(Ns + "ObsCont").Select(o => $"{o.Attribute("xCampo")?.Value}: {o.V("xTexto")}") ?? []);
        remarks.AddRange(compl?.Elements(Ns + "ObsFisco").Select(o => $"{o.Attribute("xCampo")?.Value}: {o.V("xTexto")}") ?? []);
        dto.Remarks = string.Join("\n", remarks);

        dto.Rntrc = inf.Descendants(Ns + "rodo").FirstOrDefault()?.V("RNTRC") ?? "";
        return dto;
    }

    /// <summary>Chave em grupos de 4 dígitos, como no manual.</summary>
    public static string FormatKey(string key)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < key.Length; i += 4)
        {
            if (sb.Length > 0)
                sb.Append(' ');
            sb.Append(key.AsSpan(i, Math.Min(4, key.Length - i)));
        }

        return sb.ToString();
    }

    private static string? V(this XElement e, string name) => e.Element(Ns + name)?.Value.Trim();

    private static string IssuerDetails(XElement? emit)
    {
        if (emit is null)
            return "";
        var end = emit.Element(Ns + "enderEmit");
        return string.Join("\n", new[]
        {
            Join(", ", end?.V("xLgr"), end?.V("nro"), end?.V("xCpl")),
            Join(" - ", end?.V("xBairro"), Join(" - ", end?.V("xMun"), end?.V("UF")), Cep(end?.V("CEP"))),
            Join("   ", $"CNPJ: {TaxId(emit.V("CNPJ") ?? emit.V("CPF"))}", Prefix("IE: ", emit.V("IE")), Prefix("FONE: ", end?.V("fone"))),
        }.Where(s => s.Length > 0));
    }

    private static string Party(XElement? party, string addressElement)
    {
        if (party is null)
            return "";
        var end = party.Element(Ns + addressElement);
        return string.Join("\n", new[]
        {
            party.V("xNome") ?? "",
            Join(", ", end?.V("xLgr"), end?.V("nro"), end?.V("xCpl"), end?.V("xBairro")),
            Join("   ", Join(" - ", end?.V("xMun"), end?.V("UF")), Prefix("CEP: ", Cep(end?.V("CEP")))),
            Join("   ", Prefix(party.V("CPF") is null ? "CNPJ: " : "CPF: ", TaxId(party.V("CNPJ") ?? party.V("CPF"))),
                Prefix("IE: ", party.V("IE")), Prefix("FONE: ", party.V("fone"))),
        }.Where(s => s.Length > 0));
    }

    private static string Join(string separator, params string?[] parts) =>
        string.Join(separator, parts.Where(p => !string.IsNullOrWhiteSpace(p)));

    private static string? Prefix(string prefix, string? value) => string.IsNullOrWhiteSpace(value) ? null : prefix + value;

    private static string? Cep(string? cep) => cep is { Length: 8 } ? $"{cep[..5]}-{cep[5..]}" : cep;

    private static string? TaxId(string? id) => id switch
    {
        { Length: 14 } => $"{id[..2]}.{id[2..5]}.{id[5..8]}/{id[8..12]}-{id[12..]}",
        { Length: 11 } => $"{id[..3]}.{id[3..6]}.{id[6..9]}-{id[9..]}",
        _ => id,
    };

    private static string Money(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d.ToString("N2", PtBr) : "";

    private static string Quantity(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d.ToString("#,##0.####", PtBr) : "";

    private static string Unit(string? code) => code switch
    {
        "00" => "M3", "01" => "KG", "02" => "TON", "03" => "UN", "04" => "L", "05" => "MMBTU", _ => code ?? "",
    };

    private static string Date(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.ToString("dd/MM/yyyy HH:mm:ss", PtBr)
            : value ?? "";
}
