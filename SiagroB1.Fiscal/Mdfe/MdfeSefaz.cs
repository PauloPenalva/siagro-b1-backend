using System.Globalization;
using System.Xml;
using SiagroB1.Fiscal.Nfe;

namespace SiagroB1.Fiscal.Mdfe;

/// <summary>
/// Encerramento do MDF-e (evento 110112). <see cref="UfCode"/> e <see cref="CityIbge"/> = códigos IBGE do
/// local de encerramento; <see cref="EventAt"/> em Brasília.
/// </summary>
public sealed record MdfeCloseRequest(
    string AccessKey, string Protocol, string IssuerCnpj, DateOnly ClosedOn, string UfCode, string CityIbge,
    DateTimeOffset EventAt);

/// <summary>Cancelamento do MDF-e (evento 110111). <see cref="EventAt"/> em Brasília.</summary>
public sealed record MdfeCancelRequest(
    string AccessKey, string Protocol, string Justification, string IssuerCnpj, DateTimeOffset EventAt);

/// <summary>Inclusão de condutor (evento 110114), com a sequência do evento.</summary>
public sealed record MdfeIncludeDriverRequest(
    string AccessKey, string IssuerCnpj, int Sequence, string Cpf, string Name, DateTimeOffset EventAt);

/// <summary>Um MDF-e autorizado e ainda não encerrado do emitente (consulta "não encerrados").</summary>
public sealed record MdfeNotClosed(string AccessKey, string Protocol);

/// <summary>A SEFAZ respondeu, mas recusou a consulta (status fora de 111/112).</summary>
public sealed class MdfeSefazRejectedException(int statusCode, string reason)
    : Exception($"A SEFAZ recusou a consulta do MDF-e: {statusCode} - {reason}")
{
    public int StatusCode { get; } = statusCode;
    public string Reason { get; } = reason;
}

/// <summary>
/// SEFAZ do MDF-e 3.00. Reaproveita os resultados e os códigos da NF-e (<see cref="NfeStatusCodes"/>);
/// sem resposta (rede, tempo esgotado) lança <see cref="NfeCommunicationException"/>.
/// Implementação real: <see cref="ZeusMdfeSefazClient"/>.
/// </summary>
public interface IMdfeSefazClient
{
    /// <summary>Envio síncrono (MDFeRecepcaoSinc): o retorno já traz o protocolo, sem recibo.</summary>
    Task<NfeSefazResult> AuthorizeAsync(SignedMdfe mdfe, MdfeServiceSettings settings, CancellationToken cancellationToken = default);

    Task<NfeSefazResult> ConsultProtocolAsync(string accessKey, MdfeServiceSettings settings, CancellationToken cancellationToken = default);

    Task<NfeSefazResult> ServiceStatusAsync(MdfeServiceSettings settings, CancellationToken cancellationToken = default);

    Task<NfeEventResult> CloseAsync(MdfeCloseRequest request, MdfeServiceSettings settings, CancellationToken cancellationToken = default);

    Task<NfeEventResult> CancelAsync(MdfeCancelRequest request, MdfeServiceSettings settings, CancellationToken cancellationToken = default);

    Task<NfeEventResult> IncludeDriverAsync(MdfeIncludeDriverRequest request, MdfeServiceSettings settings, CancellationToken cancellationToken = default);

    /// <summary>
    /// MDF-e autorizados e não encerrados do emitente (111 = lista; 112 = nenhum, lista vazia).
    /// Outro status lança <see cref="MdfeSefazRejectedException"/>.
    /// </summary>
    Task<IReadOnlyList<MdfeNotClosed>> ListNotClosedAsync(string issuerCnpj, MdfeServiceSettings settings, CancellationToken cancellationToken = default);
}

/// <summary><c>mdfeProc</c> = MDF-e assinado + <c>protMDFe</c>, por texto (a parte assinada fica byte a byte igual).</summary>
public static class MdfeProc
{
    public static string Compose(string signedXml, string protocolXml) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
        $"<mdfeProc versao=\"3.00\" xmlns=\"{MdfeSefazResponseMapper.Namespace}\">" + Body(signedXml) + Body(protocolXml) + "</mdfeProc>";

    private static string Body(string xml)
    {
        var text = xml.Trim();
        if (text.StartsWith("<?xml", StringComparison.Ordinal))
            text = text[(text.IndexOf("?>", StringComparison.Ordinal) + 2)..].Trim();
        return text;
    }
}

/// <summary>
/// Lê os retornos crus do MDF-e (sem as classes da Zeus): os XMLs guardados (protMDFe, procEventoMDFe)
/// são o texto original da SEFAZ, nunca re-serializado. Mesmo desenho do mapper do CT-e.
/// </summary>
internal static class MdfeSefazResponseMapper
{
    public const string Namespace = "http://www.portalfiscal.inf.br/mdfe";

    private const int CancellationEvent = 110111;
    private const int NotClosedFound = 111;
    private const int NotClosedNone = 112;

    /// <summary>retMDFe: com protMDFe vale o protocolo do MDF-e; sem ele, o status do topo.</summary>
    public static NfeSefazResult FromAuthorization(string xml)
    {
        var root = Load(xml, "retMDFe");
        var protocol = Child(root, "protMDFe");

        return protocol is not null && Child(protocol, "infProt") is not null ? FromProtocol(protocol) : Top(root);
    }

    /// <summary>retConsSitMDFe: protocolo quando autorizado/denegado; cancelado traz o evento de cancelamento registrado.</summary>
    public static NfeSefazResult FromConsult(string xml)
    {
        var root = Load(xml, "retConsSitMDFe");
        var code = Int(root, "cStat");

        var protocol = Child(root, "protMDFe");
        if (protocol is not null && Child(protocol, "infProt") is not null
            && (NfeStatusCodes.IsAuthorized(code) || NfeStatusCodes.IsDenied(code)))
            return FromProtocol(protocol);

        var cancellation = NfeStatusCodes.IsCancelledConsult(code)
            ? Children(root, "procEventoMDFe")
                .Where(e => EventType(e) == CancellationEvent && IsRegistered(e))
                .Select(FromProcEvent)
                .FirstOrDefault()
            : null;

        return Top(root) with { CancellationEvent = cancellation };
    }

    public static NfeSefazResult FromStatus(string xml) => Top(Load(xml, "retConsStatServMDFe"));

    /// <summary>retConsMDFeNaoEnc: 111 lista os MDF-e; 112 = nenhum; outro status = recusa.</summary>
    public static IReadOnlyList<MdfeNotClosed> FromNotClosed(string xml)
    {
        var root = Load(xml, "retConsMDFeNaoEnc");
        var code = Int(root, "cStat");

        return code switch
        {
            NotClosedNone => [],
            NotClosedFound => Children(root, "infMDFe")
                .Select(i => new MdfeNotClosed(Text(i, "chMDFe") ?? string.Empty, Text(i, "nProt") ?? string.Empty))
                .ToList(),
            _ => throw new MdfeSefazRejectedException(code, Text(root, "xMotivo") ?? string.Empty),
        };
    }

    /// <summary>
    /// retEventoMDFe do evento enviado. Registrado (135/136): o comprovante é o procEventoMDFe com o
    /// evento ASSINADO enviado + o retorno, ambos verbatim.
    /// </summary>
    public static NfeEventResult FromEvent(string sentEventXml, string returnXml)
    {
        var ret = Load(returnXml, "retEventoMDFe");
        var info = Child(ret, "infEvento") ?? throw Unreadable("retEventoMDFe");
        var sent = TryLoad(sentEventXml, "eventoMDFe");
        var sentInfo = sent is null ? null : Child(sent, "infEvento");
        var detail = sentInfo is null ? null : Child(sentInfo, "detEvento")?.ChildNodes.OfType<XmlElement>().FirstOrDefault();

        var code = Int(info, "cStat");
        var proc = NfeStatusCodes.IsEventRegistered(code) && sent is not null
            ? $"<procEventoMDFe versao=\"3.00\" xmlns=\"{Namespace}\">{sent.OuterXml}{ret.OuterXml}</procEventoMDFe>"
            : null;

        return new NfeEventResult(
            code, Text(info, "xMotivo") ?? string.Empty, Text(info, "nProt"), Date(info, "dhRegEvento"), proc,
            detail is null ? null : Text(detail, "xJust"),
            IntOrNull(sentInfo, "nSeqEvento") ?? IntOrNull(info, "nSeqEvento"));
    }

    private static NfeEventResult FromProcEvent(XmlElement proc)
    {
        var sent = Child(proc, "eventoMDFe");
        var ret = Child(proc, "retEventoMDFe")!;
        return FromEvent(sent?.OuterXml ?? string.Empty, ret.OuterXml) with { ProcEventXml = proc.OuterXml };
    }

    private static int? EventType(XmlElement proc)
    {
        var info = Child(proc, "retEventoMDFe") is { } ret ? Child(ret, "infEvento") : null;
        var sent = Child(proc, "eventoMDFe") is { } ev ? Child(ev, "infEvento") : null;
        return IntOrNull(sent, "tpEvento") ?? IntOrNull(info, "tpEvento");
    }

    private static bool IsRegistered(XmlElement proc) =>
        Child(proc, "retEventoMDFe") is { } ret && Child(ret, "infEvento") is { } info
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
    private static XmlElement Load(string xml, string localName) => TryLoad(xml, localName) ?? throw Unreadable(localName);

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
        new($"A SEFAZ não devolveu um {element} legível. Consulte o MDF-e antes de transmitir de novo.");

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
