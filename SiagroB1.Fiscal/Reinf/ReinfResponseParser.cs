using System.Xml.Linq;

namespace SiagroB1.Fiscal.Reinf;

/// <summary>Interpreta o retornoLoteEventosAssincrono; o evento interno (evtTotal/evtTotalContrib) é lido por local-name.</summary>
public static class ReinfResponseParser
{
    public static ReinfBatchResult Parse(string xml)
    {
        var doc = XDocument.Parse(xml);
        var root = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "retornoLoteEventosAssincrono")
            ?? throw new FormatException("Retorno da EFD-Reinf sem retornoLoteEventosAssincrono.");

        var status = Child(root, "status");
        var code = int.TryParse(Child(status, "cdResposta")?.Value.Trim(), out var c) ? c : 0;
        var occurrences = Child(status, "ocorrencias")?.Elements().Where(o => o.Name.LocalName == "ocorrencia")
            .Select(o => $"{Child(o, "codigo")?.Value.Trim()} - {Child(o, "descricao")?.Value.Trim()}").ToList() ?? [];
        var protocol = Child(Child(root, "dadosRecepcaoLote"), "protocoloEnvio")?.Value.Trim();

        var events = new List<ReinfEventResult>();
        var list = Child(root, "retornoEventos");
        if (list is not null)
        {
            foreach (var ev in list.Elements().Where(e => e.Name.LocalName == "evento"))
            {
                var id = (string?)ev.Attribute("Id") ?? "";
                var inner = ev.Descendants().FirstOrDefault(e => e.Name.LocalName is "evtTotal" or "evtTotalContrib");
                var ideStatus = inner?.Descendants().FirstOrDefault(e => e.Name.LocalName == "ideStatus");
                var accepted = Child(ideStatus, "cdRetorno")?.Value.Trim() == "0";
                var receipt = inner?.Descendants().FirstOrDefault(e => e.Name.LocalName == "nrRecArqBase")?.Value.Trim();
                var evOcc = ideStatus?.Elements().Where(e => e.Name.LocalName == "regOcorrs")
                    .Select(o => $"{Child(o, "codResp")?.Value.Trim()} - {Child(o, "dscResp")?.Value.Trim()}").ToList() ?? [];
                var raw = (ev.Descendants().FirstOrDefault(e => e.Name.LocalName == "retornoEvento")?.FirstNode ?? ev).ToString();
                events.Add(new ReinfEventResult(id, accepted, string.IsNullOrEmpty(receipt) ? null : receipt, evOcc, raw));
            }
        }

        return new ReinfBatchResult(code, Child(status, "descResposta")?.Value.Trim(), protocol, occurrences, events, xml);
    }

    private static XElement? Child(XElement? parent, string name) =>
        parent?.Elements().FirstOrDefault(e => e.Name.LocalName == name);
}
