using System.Xml;
using System.Xml.Linq;

namespace SiagroB1.Fiscal.Reinf.Events;

public static class R2099Builder
{
    public static XmlDocument Build(R2099Input i, string eventId)
    {
        XNamespace ns = ReinfNamespaces.Fechamento;
        var ideEvento = new XElement(ns + "ideEvento", new XElement(ns + "perApur", i.Period));
        foreach (var e in ReinfXml.EmissionInfo(ns, i.H)) ideEvento.Add(e);

        var resp = new XElement(ns + "ideRespInf",
            new XElement(ns + "nmResp", i.RespName),
            new XElement(ns + "cpfResp", ReinfFormat.Digits(i.RespCpf)),
            ReinfXml.Opt(ns, "telefone", i.RespPhone),
            ReinfXml.Opt(ns, "email", i.RespEmail));

        var fech = new XElement(ns + "infoFech",
            new XElement(ns + "evtServTm", "N"),
            new XElement(ns + "evtServPr", "N"),
            new XElement(ns + "evtAssDespRec", "N"),
            new XElement(ns + "evtAssDespRep", "N"),
            new XElement(ns + "evtComProd", "N"),
            new XElement(ns + "evtCPRB", "N"),
            new XElement(ns + "evtAquis", ReinfXml.SN(i.HasAcquisitions)));

        return ReinfXml.ToXmlDocument(ReinfXml.Root(ns, "evtFechaEvPer", eventId, ideEvento, ReinfXml.IdeContri(ns, i.H), resp, fech));
    }
}
