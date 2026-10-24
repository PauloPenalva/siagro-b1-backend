using System.Text;
using System.Xml;

namespace SiagroB1.Fiscal.Reinf;

/// <summary>Monta o lote assíncrono (envioLoteEventos) com até 50 eventos já assinados.</summary>
public static class ReinfBatchBuilder
{
    public const int MaxEvents = 50;

    public static string Build(string taxIdRoot, IReadOnlyList<(string EventId, XmlDocument Signed)> events)
    {
        if (events.Count == 0) throw new ArgumentException("O lote precisa de ao menos um evento.", nameof(events));
        if (events.Count > MaxEvents)
            throw new ArgumentException($"O lote aceita no máximo {MaxEvents} eventos.", nameof(events));

        const string ns = ReinfNamespaces.EnvioLote;
        var doc = new XmlDocument { PreserveWhitespace = true };
        var reinf = doc.AppendChild(doc.CreateElement("Reinf", ns))!;
        var envio = reinf.AppendChild(doc.CreateElement("envioLoteEventos", ns))!;
        var ide = envio.AppendChild(doc.CreateElement("ideContribuinte", ns))!;
        ide.AppendChild(doc.CreateElement("tpInsc", ns))!.InnerText = "1";
        ide.AppendChild(doc.CreateElement("nrInsc", ns))!.InnerText = taxIdRoot;
        var eventos = envio.AppendChild(doc.CreateElement("eventos", ns))!;

        foreach (var (eventId, signed) in events)
        {
            var evento = doc.CreateElement("evento", ns);
            evento.SetAttribute("Id", eventId);
            evento.AppendChild(doc.ImportNode(signed.DocumentElement!, true));
            eventos.AppendChild(evento);
        }

        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = false,
            OmitXmlDeclaration = false,
        };
        using var ms = new MemoryStream();
        using (var writer = XmlWriter.Create(ms, settings)) doc.Save(writer);
        return new UTF8Encoding(false).GetString(ms.ToArray());
    }
}
