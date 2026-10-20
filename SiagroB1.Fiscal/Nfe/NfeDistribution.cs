using System.Globalization;
using System.Xml.Linq;

namespace SiagroB1.Fiscal.Nfe;

/// <summary>Tipo da manifestação do destinatário; o valor é o <c>tpEvento</c> da SEFAZ.</summary>
public enum NfeManifestationType
{
    Confirmation = 210200,
    Acknowledgement = 210210,
    Unknown = 210220,
    NotPerformed = 210240,
}

/// <summary>
/// Pedido de manifestação (evento do destinatário, Ambiente Nacional). <see cref="Justification"/> só vale na
/// Operação não Realizada (15–255 caracteres). <see cref="EventAt"/> em Brasília.
/// </summary>
public sealed record NfeManifestationRequest(
    string AccessKey, NfeManifestationType Type, string RecipientTaxId, string? Justification, DateTimeOffset EventAt);

/// <summary>Um documento do lote da distribuição. <see cref="Xml"/> já vem descompactado (o docZip é gzip+base64).</summary>
public sealed record NfeDistributionDocument(long Nsu, string Schema, string Xml);

/// <summary>
/// Retorno da NFeDistribuicaoDFe: 138 (há documentos), 137 (nenhum; esperar uma hora) ou 656 (consumo indevido).
/// <see cref="LastNsu"/>/<see cref="MaxNsu"/> são os cursores devolvidos pela SEFAZ.
/// </summary>
public sealed record NfeDistributionResult(
    int StatusCode, string Reason, long LastNsu, long MaxNsu, IReadOnlyList<NfeDistributionDocument> Documents);

public enum DfeKind { Summary, FullInvoice, Event, Other }

/// <summary>
/// Documento da distribuição já lido. <see cref="InvoiceSituation"/> é o <c>cSitNFe</c> (1 autorizada, 2 denegada,
/// 3 cancelada); <see cref="FullXml"/> só vem no procNFe (nota completa).
/// </summary>
public sealed record DfeParsed(
    DfeKind Kind, string? AccessKey, string? IssuerTaxId, string? IssuerName, DateTimeOffset? IssueDate,
    decimal? TotalValue, int? InvoiceSituation, int? EventType, string? FullXml);

/// <summary>Lê o XML de um documento da distribuição pelo nome local dos elementos (resNFe, procNFe, resEvento, procEventoNFe).</summary>
public static class NfeDistributionDocumentParser
{
    public static DfeParsed Parse(NfeDistributionDocument document)
    {
        var schema = document.Schema ?? string.Empty;

        if (schema.StartsWith("resNFe", StringComparison.OrdinalIgnoreCase))
            return ParseSummary(Load(document.Xml));

        if (schema.StartsWith("procNFe", StringComparison.OrdinalIgnoreCase))
            return ParseFullInvoice(Load(document.Xml), document.Xml);

        if (schema.StartsWith("resEvento", StringComparison.OrdinalIgnoreCase)
            || schema.StartsWith("procEventoNFe", StringComparison.OrdinalIgnoreCase))
            return ParseEvent(Load(document.Xml));

        return new DfeParsed(DfeKind.Other, null, null, null, null, null, null, null, null);
    }

    private static XElement Load(string xml) => XDocument.Parse(xml).Root!;

    private static DfeParsed ParseSummary(XElement root) => new(
        DfeKind.Summary, Text(root, "chNFe"), Text(root, "CNPJ") ?? Text(root, "CPF"), Text(root, "xNome"),
        Date(Text(root, "dhEmi")), Decimal(Text(root, "vNF")), Int(Text(root, "cSitNFe")), null, null);

    private static DfeParsed ParseFullInvoice(XElement root, string xml)
    {
        var info = Descendant(root, "infNFe");
        var emit = info is null ? null : Descendant(info, "emit");
        var key = info?.Attribute("Id")?.Value is { Length: > 3 } id && id.StartsWith("NFe", StringComparison.Ordinal)
            ? id[3..]
            : Text(root, "chNFe");

        return new DfeParsed(
            DfeKind.FullInvoice, key, emit is null ? null : Text(emit, "CNPJ") ?? Text(emit, "CPF"),
            emit is null ? null : Text(emit, "xNome"), info is null ? null : Date(Text(info, "dhEmi")),
            Decimal(Text(Descendant(root, "ICMSTot"), "vNF")), null, null, xml);
    }

    private static DfeParsed ParseEvent(XElement root) => new(
        DfeKind.Event, Text(root, "chNFe"), null, null, Date(Text(root, "dhEvento")), null, null,
        Int(Text(root, "tpEvento")), null);

    private static XElement? Descendant(XElement? parent, string name) =>
        parent?.Descendants().FirstOrDefault(e => e.Name.LocalName == name);

    private static string? Text(XElement? parent, string name)
    {
        var value = Descendant(parent, name)?.Value.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static DateTimeOffset? Date(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var at) ? at : null;

    private static decimal? Decimal(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number) ? number : null;

    private static int? Int(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : null;
}
