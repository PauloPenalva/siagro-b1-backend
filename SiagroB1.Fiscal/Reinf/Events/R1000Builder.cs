using System.Xml;
using System.Xml.Linq;

namespace SiagroB1.Fiscal.Reinf.Events;

public static class R1000Builder
{
    public static XmlDocument Build(R1000Input i, string eventId)
    {
        XNamespace ns = ReinfNamespaces.InfoContribuinte;
        var ideEvento = new XElement(ns + "ideEvento");
        foreach (var e in ReinfXml.EmissionInfo(ns, i.H)) ideEvento.Add(e);

        var periodo = new XElement(ns + "idePeriodo", new XElement(ns + "iniValid", i.ValidFrom));

        XElement operation = i.Operation == R1000Operation.Delete
            ? new XElement(ns + "exclusao", periodo)
            : new XElement(ns + (i.Operation == R1000Operation.Include ? "inclusao" : "alteracao"),
                periodo, InfoCadastro(ns, i));

        return ReinfXml.ToXmlDocument(ReinfXml.Root(ns, "evtInfoContri", eventId,
            ideEvento, ReinfXml.IdeContri(ns, i.H), new XElement(ns + "infoContri", operation)));
    }

    private static XElement InfoCadastro(XNamespace ns, R1000Input i) =>
        new(ns + "infoCadastro",
            new XElement(ns + "classTrib", i.TaxClassification),
            new XElement(ns + "indEscrituracao", ReinfXml.Flag(i.KeepsBookkeeping)),
            new XElement(ns + "indDesoneracao", ReinfXml.Flag(i.PayrollRelief)),
            new XElement(ns + "indAcordoIsenMulta", ReinfXml.Flag(i.FineExemptionAgreement)),
            i.CompanySituation is { } s ? new XElement(ns + "indSitPJ", s) : null,
            new XElement(ns + "contato",
                new XElement(ns + "nmCtt", i.ContactName),
                new XElement(ns + "cpfCtt", ReinfFormat.Digits(i.ContactCpf)),
                ReinfXml.Opt(ns, "foneFixo", i.ContactPhone),
                ReinfXml.Opt(ns, "email", i.ContactEmail)),
            SoftHouse(ns, i.SoftHouse));

    private static XElement SoftHouse(XNamespace ns, SoftwareHouse s) =>
        new(ns + "softHouse",
            new XElement(ns + "cnpjSoftHouse", ReinfFormat.Digits(s.Cnpj)),
            new XElement(ns + "nmRazao", s.Name),
            new XElement(ns + "nmCont", s.Contact),
            ReinfXml.Opt(ns, "telefone", s.Phone),
            ReinfXml.Opt(ns, "email", s.Email));
}
