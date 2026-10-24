using System.Xml;
using System.Xml.Linq;

namespace SiagroB1.Fiscal.Reinf.Events;

public static class R2098Builder
{
    public static XmlDocument Build(R2098Input i, string eventId)
    {
        XNamespace ns = ReinfNamespaces.ReabreEvPer;
        var ideEvento = new XElement(ns + "ideEvento", new XElement(ns + "perApur", i.Period));
        foreach (var e in ReinfXml.EmissionInfo(ns, i.H)) ideEvento.Add(e);
        return ReinfXml.ToXmlDocument(ReinfXml.Root(ns, "evtReabreEvPer", eventId, ideEvento, ReinfXml.IdeContri(ns, i.H)));
    }
}
