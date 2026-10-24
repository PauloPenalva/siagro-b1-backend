using System.Xml;
using System.Xml.Schema;

namespace SiagroB1.Fiscal.Reinf;

/// <summary>Valida documentos da EFD-Reinf contra os XSDs oficiais (v2_01_02).</summary>
public class ReinfSchemaValidator
{
    private static readonly string SchemasDirectory =
        Path.Combine(AppContext.BaseDirectory, "Schemas", "Reinf", "v2_01_02");

    private static readonly Dictionary<string, string> FileByNamespace = new()
    {
        [ReinfNamespaces.InfoContribuinte] = "R-1000-evtInfoContribuinte.xsd",
        [ReinfNamespaces.AquisicaoProdRural] = "R-2055-evt2055AquisicaoProdRural.xsd",
        [ReinfNamespaces.ReabreEvPer] = "R-2098-evtReabreEvPer.xsd",
        [ReinfNamespaces.Fechamento] = "R-2099-evtFechamento.xsd",
        [ReinfNamespaces.Exclusao] = "R-9000-evtExclusao.xsd",
        [ReinfNamespaces.Total] = "R-9001-evtTotal.xsd",
        [ReinfNamespaces.TotalContrib] = "R-9011-evtTotalContrib.xsd",
        [ReinfNamespaces.EnvioLote] = "envioLoteEventosAssincrono-v1_00_00.xsd",
        [ReinfNamespaces.RetornoLote] = "retornoLoteEventosAssincrono-v1_00_00.xsd",
    };

    // Todos os XSDs declaram o elemento raiz "Reinf"; por isso um conjunto compilado por namespace.
    private static readonly Dictionary<string, Lazy<XmlSchemaSet>> Sets =
        FileByNamespace.ToDictionary(kv => kv.Key, kv => new Lazy<XmlSchemaSet>(() => Compile(kv.Value)));

    private static XmlSchemaSet Compile(string file)
    {
        var set = new XmlSchemaSet { XmlResolver = new XmlUrlResolver() };
        set.Add(null, Path.Combine(SchemasDirectory, file));
        set.Compile();
        return set;
    }

    /// <summary>Lista vazia = válido.</summary>
    public IReadOnlyList<string> Validate(XmlDocument doc)
    {
        var root = doc.DocumentElement;
        if (root is null) return new[] { "Documento vazio." };

        var ns = root.NamespaceURI;
        if (root.LocalName == "Reinf" && ns != ReinfNamespaces.EnvioLote && ns != ReinfNamespaces.RetornoLote)
        {
            var first = root.ChildNodes.OfType<XmlElement>().FirstOrDefault();
            if (first is not null) ns = first.NamespaceURI;
        }

        if (!Sets.TryGetValue(ns, out var lazy))
            return new[] { $"Namespace desconhecido: {ns}" };

        var errors = new List<string>();
        var settings = new XmlReaderSettings
        {
            ValidationType = ValidationType.Schema,
            Schemas = lazy.Value,
            DtdProcessing = DtdProcessing.Prohibit,
        };
        settings.ValidationEventHandler += (_, e) => errors.Add(e.Message);

        using var reader = XmlReader.Create(new StringReader(doc.OuterXml), settings);
        while (reader.Read()) { }
        return errors;
    }
}
