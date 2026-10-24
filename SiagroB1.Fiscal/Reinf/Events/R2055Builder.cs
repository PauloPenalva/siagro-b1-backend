using System.Xml;
using System.Xml.Linq;

namespace SiagroB1.Fiscal.Reinf.Events;

public static class R2055Builder
{
    public static XmlDocument Build(R2055Input i, string eventId)
    {
        if (i.RectificationIndicator == 2 && string.IsNullOrWhiteSpace(i.PreviousReceipt))
            throw new InvalidOperationException("Retificação do R-2055 exige o recibo do evento original.");
        XNamespace ns = ReinfNamespaces.AquisicaoProdRural;
        var ideEvento = new XElement(ns + "ideEvento",
            new XElement(ns + "indRetif", i.RectificationIndicator),
            ReinfXml.Opt(ns, "nrRecibo", i.RectificationIndicator == 2 ? i.PreviousReceipt : null),
            new XElement(ns + "perApur", i.Period));
        foreach (var e in ReinfXml.EmissionInfo(ns, i.H)) ideEvento.Add(e);

        var produtor = new XElement(ns + "ideProdutor",
            new XElement(ns + "tpInscProd", i.ProducerInscriptionType),
            new XElement(ns + "nrInscProd", ReinfFormat.Digits(i.ProducerTaxId)),
            i.PayrollOption ? new XElement(ns + "indOpcCP", "S") : null);
        foreach (var d in i.Details) produtor.Add(Detail(ns, d));

        var info = new XElement(ns + "infoAquisProd",
            new XElement(ns + "ideEstabAdquir",
                new XElement(ns + "tpInscAdq", 1),
                new XElement(ns + "nrInscAdq", ReinfFormat.Digits(i.EstablishmentTaxId)),
                produtor));

        return ReinfXml.ToXmlDocument(ReinfXml.Root(ns, "evtAqProd", eventId, ideEvento, ReinfXml.IdeContri(ns, i.H), info));
    }

    private static XElement Detail(XNamespace ns, R2055Detail d)
    {
        var det = new XElement(ns + "detAquis",
            new XElement(ns + "indAquis", d.AcquisitionIndicator),
            new XElement(ns + "vlrBruto", ReinfFormat.Money(d.Gross)),
            new XElement(ns + "vlrCPDescPR", ReinfFormat.Money(d.Inss)),
            new XElement(ns + "vlrRatDescPR", ReinfFormat.Money(d.Rat)),
            new XElement(ns + "vlrSenarDesc", ReinfFormat.Money(d.Senar)));
        if (!string.IsNullOrWhiteSpace(d.JudicialProcess))
        {
            det.Add(new XElement(ns + "infoProcJud",
                new XElement(ns + "nrProcJud", ReinfFormat.Digits(d.JudicialProcess)),
                ReinfXml.Opt(ns, "codSusp", ReinfFormat.Digits(d.SuspensionCode)),
                d.InssNotWithheld > 0 ? new XElement(ns + "vlrCPNRet", ReinfFormat.Money(d.InssNotWithheld)) : null,
                d.RatNotWithheld > 0 ? new XElement(ns + "vlrRatNRet", ReinfFormat.Money(d.RatNotWithheld)) : null));
        }
        return det;
    }
}
