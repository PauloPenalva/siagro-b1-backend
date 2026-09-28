using System.Net;

namespace SiagroB1.Application.Services.ContractDrafts;

/// <summary>
/// Embrulha o BodyHtml da minuta num documento imprimível. O CSS é o MESMO que o editor UI5
/// aplica no preview — o que se vê é o que se imprime. Se mudar aqui, mude lá.
/// </summary>
public static class ContractDraftPdfLayout
{
    public const string Css =
        "body{font-family:Arial,Helvetica,sans-serif;font-size:11pt;line-height:1.45;color:#000}" +
        "h1,h2,h3{font-weight:bold}h1{font-size:14pt;text-align:center}h2{font-size:12pt}" +
        "p{margin:0 0 8pt;text-align:justify}" +
        "table{border-collapse:collapse;width:100%;margin:6pt 0}td,th{padding:2pt 4pt;vertical-align:top}" +
        "table.signature{width:auto;margin:18pt 0 6pt}table.signature td:first-child{font-weight:bold;padding-right:8pt}";

    public static string Wrap(string bodyHtml, string title) =>
        "<!DOCTYPE html><html lang=\"pt-BR\"><head><meta charset=\"utf-8\">" +
        $"<title>{WebUtility.HtmlEncode(title)}</title><style>{Css}</style></head>" +
        $"<body>{bodyHtml}</body></html>";
}
