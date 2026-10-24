using System.Xml;
using System.Xml.Linq;

namespace SiagroB1.Fiscal.Reinf.Events;

/// <summary>Auxiliares compartilhados pelos construtores de eventos.</summary>
internal static class ReinfXml
{
    public static XmlDocument ToXmlDocument(XElement root)
    {
        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.LoadXml(new XDocument(root).ToString(SaveOptions.DisableFormatting));
        return doc;
    }

    public static XElement Root(XNamespace ns, string evtName, string eventId, params object?[] content) =>
        new(ns + "Reinf", new XElement(ns + evtName, new XAttribute("id", eventId), content));

    /// <summary>Elemento opcional: retorna null (ignorado pelo XElement) quando o valor é vazio.</summary>
    public static XElement? Opt(XNamespace ns, string name, string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : new XElement(ns + name, value);

    public static XElement IdeContri(XNamespace ns, ReinfHeader h) =>
        new(ns + "ideContri", new XElement(ns + "tpInsc", 1), new XElement(ns + "nrInsc", h.TaxIdRoot));

    /// <summary>tpAmb, procEmi, verProc.</summary>
    public static object[] EmissionInfo(XNamespace ns, ReinfHeader h) =>
    [
        new XElement(ns + "tpAmb", (int)h.Environment),
        new XElement(ns + "procEmi", 1),
        new XElement(ns + "verProc", h.AppVersion),
    ];

    public static string Flag(bool v) => v ? "1" : "0";
    public static string SN(bool v) => v ? "S" : "N";
}
