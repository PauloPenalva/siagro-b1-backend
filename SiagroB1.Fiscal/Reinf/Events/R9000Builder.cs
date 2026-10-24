using System.Xml;
using System.Xml.Linq;

namespace SiagroB1.Fiscal.Reinf.Events;

public static class R9000Builder
{
    public static XmlDocument Build(R9000Input i, string eventId)
    {
        XNamespace ns = ReinfNamespaces.Exclusao;
        var ideEvento = new XElement(ns + "ideEvento");
        foreach (var e in ReinfXml.EmissionInfo(ns, i.H)) ideEvento.Add(e);
        var info = new XElement(ns + "infoExclusao",
            new XElement(ns + "tpEvento", i.EventType),
            new XElement(ns + "nrRecEvt", i.Receipt),
            new XElement(ns + "perApur", i.Period));
        return ReinfXml.ToXmlDocument(ReinfXml.Root(ns, "evtExclusao", eventId, ideEvento, ReinfXml.IdeContri(ns, i.H), info));
    }
}
