using System.Globalization;
using System.Xml;
using SiagroB1.Fiscal.Nfe;

namespace SiagroB1.Fiscal.Cte;

/// <summary>Pedido de cancelamento do CT-e (evento 110111). <see cref="EventAt"/> em Brasília.</summary>
public sealed record CteCancelRequest(
    string AccessKey, string Protocol, string Justification, string IssuerCnpj, DateTimeOffset EventAt);

/// <summary>Um item da CC-e estruturada do CT-e (grupo/campo/valor do leiaute; <see cref="GroupNumber"/> só em grupo repetido).</summary>
public sealed record CteCorrectionItem(string Group, string Field, string Value, int? GroupNumber);

/// <summary>Pedido de CC-e do CT-e (evento 110110), com a sequência da carta.</summary>
public sealed record CteCorrectionRequest(
    string AccessKey, string IssuerCnpj, int Sequence, IReadOnlyList<CteCorrectionItem> Items, DateTimeOffset EventAt);

/// <summary>Pedido de inutilização de uma faixa (<see cref="From"/>–<see cref="To"/>) da série. <see cref="StateCode"/> = código IBGE da UF.</summary>
public sealed record CteVoidNumberRequest(
    string IssuerCnpj, string StateCode, int Year, int Series, int From, int To, string Justification);

/// <summary>
/// SEFAZ do CT-e 4.00. Reaproveita os resultados e os códigos da NF-e (<see cref="NfeStatusCodes"/>);
/// sem resposta (rede, tempo esgotado) lança <see cref="NfeCommunicationException"/>.
/// Implementação real: <see cref="ZeusCteSefazClient"/>.
/// </summary>
public interface ICteSefazClient
{
    /// <summary>Envio síncrono (CTeRecepcaoSincV4): o retorno já traz o protocolo, sem recibo.</summary>
    Task<NfeSefazResult> AuthorizeAsync(SignedCte cte, CteServiceSettings settings, CancellationToken cancellationToken = default);

    Task<NfeSefazResult> ConsultProtocolAsync(string accessKey, CteServiceSettings settings, CancellationToken cancellationToken = default);

    Task<NfeSefazResult> ServiceStatusAsync(CteServiceSettings settings, CancellationToken cancellationToken = default);

    Task<NfeEventResult> CancelAsync(CteCancelRequest request, CteServiceSettings settings, CancellationToken cancellationToken = default);

    Task<NfeEventResult> SendCorrectionAsync(CteCorrectionRequest request, CteServiceSettings settings, CancellationToken cancellationToken = default);

    Task<NfeVoidNumberResult> VoidNumberAsync(CteVoidNumberRequest request, CteServiceSettings settings, CancellationToken cancellationToken = default);
}

/// <summary>
/// Lê os retornos crus do CT-e (sem as classes da Zeus): os XMLs guardados (protCTe, procEventoCTe,
/// procInutCTe) são o texto original da SEFAZ, nunca re-serializado — data com fuso e assinatura intactas.
/// </summary>
internal static class CteSefazResponseMapper
{
    public const string Namespace = "http://www.portalfiscal.inf.br/cte";

    private const int CancellationEvent = 110111;
    private const int CorrectionEvent = 110110;

    /// <summary>retCTe: com protCTe vale o protocolo do CT-e; sem ele, o status do topo.</summary>
    public static NfeSefazResult FromAuthorization(string xml)
    {
        var root = Load(xml, "retCTe");
        var protocol = Child(root, "protCTe");

        return protocol is not null && Child(protocol, "infProt") is not null
            ? FromProtocol(protocol)
            : Top(root);
    }

    /// <summary>
    /// retConsSitCTe: protocolo só quando o topo é de autorização/denegação; cancelado (101/151) traz o
    /// evento de cancelamento registrado; CC-e registradas vêm em ordem de sequência.
    /// </summary>
    public static NfeSefazResult FromConsult(string xml)
    {
        var root = Load(xml, "retConsSitCTe");
        var code = Int(root, "cStat");
        var events = Children(root, "procEventoCTe").ToList();

        var corrections = events
            .Where(e => EventType(e) == CorrectionEvent && IsRegistered(e))
            .Select(FromProcEvent)
            .OrderBy(e => e.Sequence)
            .ToList();

        var protocol = Child(root, "protCTe");
        if (protocol is not null && Child(protocol, "infProt") is not null
            && (NfeStatusCodes.IsAuthorized(code) || NfeStatusCodes.IsDenied(code)))
            return FromProtocol(protocol) with { Corrections = corrections };

        var cancellation = NfeStatusCodes.IsCancelledConsult(code)
            ? events.Where(e => EventType(e) == CancellationEvent && IsRegistered(e)).Select(FromProcEvent).FirstOrDefault()
            : null;

        return Top(root) with { CancellationEvent = cancellation, Corrections = corrections };
    }

    public static NfeSefazResult FromStatus(string xml) => Top(Load(xml, "retConsStatServCTe"));

    /// <summary>
    /// retEventoCTe do evento enviado. Registrado (135/155): o comprovante é o procEventoCTe com o
    /// evento ASSINADO enviado + o retorno, ambos verbatim.
    /// </summary>
    public static NfeEventResult FromEvent(string sentEventXml, string returnXml)
    {
        var ret = Load(returnXml, "retEventoCTe");
        var info = Child(ret, "infEvento") ?? throw Unreadable("retEventoCTe");
        var sent = TryLoad(sentEventXml, "eventoCTe");
        var sentInfo = sent is null ? null : Child(sent, "infEvento");
        var detail = sentInfo is null ? null : Child(sentInfo, "detEvento")?.FirstChild as XmlElement;

        var code = Int(info, "cStat");
        var proc = NfeStatusCodes.IsEventRegistered(code) && sent is not null
            ? $"<procEventoCTe versao=\"4.00\" xmlns=\"{Namespace}\">{sent.OuterXml}{ret.OuterXml}</procEventoCTe>"
            : null;

        return new NfeEventResult(
            code, Text(info, "xMotivo") ?? string.Empty, Text(info, "nProt"), Date(info, "dhRegEvento"), proc,
            detail is null ? null : Text(detail, "xJust"),
            IntOrNull(sentInfo, "nSeqEvento") ?? IntOrNull(info, "nSeqEvento"));
    }

    /// <summary>retInutCTe. Homologada (102): o comprovante é o procInutCTe (pedido assinado + retorno, verbatim).</summary>
    public static NfeVoidNumberResult FromVoidNumber(string? requestXml, string returnXml)
    {
        var ret = Load(returnXml, "retInutCTe");
        var info = Child(ret, "infInut") ?? throw Unreadable("retInutCTe");
        var code = Int(info, "cStat");
        var reason = Text(info, "xMotivo") ?? string.Empty;

        if (code != NfeStatusCodes.NumberVoided)
            return new NfeVoidNumberResult(code, reason);

        var request = TryLoad(requestXml, "inutCTe");
        var proc = request is null
            ? null
            : $"<procInutCTe versao=\"4.00\" xmlns=\"{Namespace}\">{request.OuterXml}{ret.OuterXml}</procInutCTe>";

        return new NfeVoidNumberResult(code, reason, Text(info, "nProt"), proc);
    }

    private static NfeEventResult FromProcEvent(XmlElement proc)
    {
        var sent = Child(proc, "eventoCTe");
        var ret = Child(proc, "retEventoCTe")!;

        // Reaproveita a leitura do envio: o procEventoCTe da consulta já é o comprovante.
        var result = FromEvent(sent?.OuterXml ?? string.Empty, ret.OuterXml);
        return result with { ProcEventXml = proc.OuterXml };
    }

    private static int? EventType(XmlElement proc)
    {
        var info = Child(proc, "retEventoCTe") is { } ret ? Child(ret, "infEvento") : null;
        var sent = Child(proc, "eventoCTe") is { } ev ? Child(ev, "infEvento") : null;
        return IntOrNull(sent, "tpEvento") ?? IntOrNull(info, "tpEvento");
    }

    private static bool IsRegistered(XmlElement proc) =>
        Child(proc, "retEventoCTe") is { } ret && Child(ret, "infEvento") is { } info
                                               && NfeStatusCodes.IsEventRegistered(Int(info, "cStat"));

    private static NfeSefazResult FromProtocol(XmlElement protocol)
    {
        var info = Child(protocol, "infProt")!;
        return new NfeSefazResult(
            Int(info, "cStat"), Text(info, "xMotivo") ?? string.Empty, Text(info, "nProt"), Date(info, "dhRecbto"),
            protocol.OuterXml);
    }

    private static NfeSefazResult Top(XmlElement root) => new(Int(root, "cStat"), Text(root, "xMotivo") ?? string.Empty);

    /// <summary>Acha o elemento pelo nome local em qualquer profundidade (o retorno pode vir dentro do envelope SOAP).</summary>
    private static XmlElement Load(string xml, string localName) =>
        TryLoad(xml, localName) ?? throw Unreadable(localName);

    private static XmlElement? TryLoad(string? xml, string localName)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return null;

        try
        {
            var document = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
            document.LoadXml(xml);
            return document.DocumentElement is { } root && root.LocalName == localName && root.NamespaceURI == Namespace
                ? root
                : document.GetElementsByTagName(localName, Namespace).Cast<XmlElement>().FirstOrDefault();
        }
        catch (XmlException)
        {
            return null;
        }
    }

    private static NfeCommunicationException Unreadable(string element) =>
        new($"A SEFAZ não devolveu um {element} legível. Consulte o CT-e antes de transmitir de novo.");

    private static XmlElement? Child(XmlElement parent, string localName) =>
        parent.ChildNodes.OfType<XmlElement>().FirstOrDefault(e => e.LocalName == localName);

    private static IEnumerable<XmlElement> Children(XmlElement parent, string localName) =>
        parent.ChildNodes.OfType<XmlElement>().Where(e => e.LocalName == localName);

    private static string? Text(XmlElement parent, string localName) => Child(parent, localName)?.InnerText;

    private static int Int(XmlElement parent, string localName) => IntOrNull(parent, localName) ?? 0;

    private static int? IntOrNull(XmlElement? parent, string localName) =>
        parent is not null && int.TryParse(Text(parent, localName), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static DateTimeOffset? Date(XmlElement parent, string localName) =>
        DateTimeOffset.TryParse(Text(parent, localName), CultureInfo.InvariantCulture, DateTimeStyles.None, out var at) ? at : null;
}
