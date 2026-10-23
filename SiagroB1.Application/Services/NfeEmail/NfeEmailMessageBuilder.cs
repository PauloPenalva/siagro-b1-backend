using System.Globalization;
using System.Net;
using System.Text;
using System.Xml.Linq;
using SiagroB1.Domain.Dtos.Email;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Services.NfeEmail;

/// <summary>
/// Monta o e-mail da NF-e a partir do procNFe autorizado. Autorizada: procNFe + DANFE. Cancelada: procEventoNFe +
/// DANFE (já com a marca CANCELADO); sem o procEvento (cancelada fora do Siagro), só o DANFE e um aviso no corpo.
/// </summary>
public static class NfeEmailMessageBuilder
{
    private static readonly XNamespace Ns = "http://www.portalfiscal.inf.br/nfe";
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static (string Subject, string HtmlBody, IReadOnlyList<EmailAttachment> Attachments) Build(
        NfeEmailEventType evt, string procXml, string? eventXml, byte[] danfe)
    {
        var doc = XDocument.Parse(procXml);
        string? Value(string name) => doc.Descendants(Ns + name).FirstOrDefault()?.Value;
        string? Child(string parent, string name) =>
            doc.Descendants(Ns + parent).FirstOrDefault()?.Element(Ns + name)?.Value;

        var number = Value("nNF") ?? string.Empty;
        var series = Value("serie") ?? string.Empty;
        var accessKey = Value("chNFe")
                        ?? doc.Descendants(Ns + "infNFe").FirstOrDefault()?.Attribute("Id")?.Value.Replace("NFe", "")
                        ?? string.Empty;
        var emitterName = Child("emit", "xNome") ?? string.Empty;
        var emitterCnpj = Child("emit", "CNPJ") ?? Child("emit", "CPF") ?? string.Empty;
        var recipientName = Child("dest", "xNome") ?? string.Empty;
        var protocol = Value("nProt") ?? string.Empty;
        var issuedAt = DateTimeOffset.TryParse(Value("dhEmi"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.ToString("dd/MM/yyyy", PtBr) : string.Empty;
        var total = decimal.TryParse(Value("vNF"), NumberStyles.Number, CultureInfo.InvariantCulture, out var v)
            ? v.ToString("N2", PtBr) : string.Empty;

        var cancelled = evt == NfeEmailEventType.Cancelled;
        var subject = $"NF-e nº {number} série {series} – {emitterName} – {(cancelled ? "Cancelada" : "Autorizada")}";

        var attachments = new List<EmailAttachment>();
        if (!cancelled)
            attachments.Add(new EmailAttachment($"{accessKey}-procNFe.xml", Encoding.UTF8.GetBytes(procXml), "application/xml"));
        else if (eventXml is not null)
            attachments.Add(new EmailAttachment($"{accessKey}-procEventoNFe.xml", Encoding.UTF8.GetBytes(eventXml), "application/xml"));
        attachments.Add(new EmailAttachment($"DANFE-{number}.pdf", danfe, "application/pdf"));

        static string E(string s) => WebUtility.HtmlEncode(s);

        var body = new StringBuilder();
        body.Append("<p>Prezado(a),</p>");
        body.Append(cancelled
            ? $"<p>Informamos que a NF-e nº {E(number)} série {E(series)} emitida por {E(emitterName)} foi <b>cancelada</b>.</p>"
            : $"<p>Segue a NF-e nº {E(number)} série {E(series)} emitida por {E(emitterName)}, autorizada pela SEFAZ.</p>");
        body.Append("<table cellpadding=\"4\">");
        body.Append($"<tr><td>Emitente</td><td>{E(emitterName)} (CNPJ/CPF {E(emitterCnpj)})</td></tr>");
        body.Append($"<tr><td>Destinatário</td><td>{E(recipientName)}</td></tr>");
        body.Append($"<tr><td>Emissão</td><td>{E(issuedAt)}</td></tr>");
        body.Append($"<tr><td>Valor total</td><td>R$ {E(total)}</td></tr>");
        body.Append($"<tr><td>Chave de acesso</td><td>{E(accessKey)}</td></tr>");
        body.Append($"<tr><td>Protocolo de autorização</td><td>{E(protocol)}</td></tr>");
        body.Append("</table>");

        if (cancelled && eventXml is null)
            body.Append("<p>Esta nota foi cancelada fora do Siagro; o XML do evento de cancelamento não está disponível. " +
                        "Consulte a situação pela chave de acesso no portal da SEFAZ.</p>");

        body.Append("<p>Este é um e-mail automático; não responda.</p>");

        return (subject, body.ToString(), attachments);
    }

    private static readonly XNamespace CteNs = "http://www.portalfiscal.inf.br/cte";

    /// <summary>
    /// E-mail do CT-e. Autorizado: <c>CTe{chave}.xml</c> (cteProc) + <c>DACTE{chave}.pdf</c>. Cancelado: o cteProc, o
    /// procEventoCTe (quando existir) como <c>CTeCancelamento{chave}.xml</c> e o DACTE (já com a tarja de cancelado).
    /// </summary>
    public static (string Subject, string HtmlBody, IReadOnlyList<EmailAttachment> Attachments) BuildCte(
        NfeEmailEventType evt, int? number, int series, string accessKey, string procXml, string? eventXml, byte[] dacte)
    {
        var emitterName = XDocument.Parse(procXml).Descendants(CteNs + "emit").FirstOrDefault()?.Element(CteNs + "xNome")?.Value
                          ?? string.Empty;
        var cancelled = evt == NfeEmailEventType.Cancelled;
        var subject = $"{(cancelled ? "Cancelamento do CT-e" : "CT-e")} {number}/{series} - {emitterName}";

        var attachments = new List<EmailAttachment>
        {
            new($"CTe{accessKey}.xml", Encoding.UTF8.GetBytes(procXml), "application/xml"),
        };
        if (cancelled && eventXml is not null)
            attachments.Add(new EmailAttachment($"CTeCancelamento{accessKey}.xml", Encoding.UTF8.GetBytes(eventXml), "application/xml"));
        attachments.Add(new EmailAttachment($"DACTE{accessKey}.pdf", dacte, "application/pdf"));

        static string E(string s) => WebUtility.HtmlEncode(s);

        var body = new StringBuilder();
        body.Append("<p>Prezado(a),</p>");
        body.Append(cancelled
            ? $"<p>Informamos que o CT-e nº {E(number?.ToString() ?? "")} série {series} emitido por {E(emitterName)} foi <b>cancelado</b>.</p>"
            : $"<p>Segue o CT-e nº {E(number?.ToString() ?? "")} série {series} emitido por {E(emitterName)}, autorizado pela SEFAZ.</p>");
        body.Append($"<p>Chave de acesso: {E(accessKey)}</p>");
        body.Append("<p>Este é um e-mail automático; não responda.</p>");

        return (subject, body.ToString(), attachments);
    }

}
