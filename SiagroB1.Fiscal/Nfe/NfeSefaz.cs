using System.Globalization;
using DFe.Utils;
using NFe.Classes.Protocolo;
using NFe.Classes.Servicos.Consulta;
using NFe.Classes.Servicos.Evento;
using NFe.Classes.Servicos.Recepcao;
using NFe.Classes.Servicos.Status;
using NFe.Classes.Servicos.Tipos;

namespace SiagroB1.Fiscal.Nfe;

/// <summary>
/// Resposta da SEFAZ sem tipos da Zeus. <see cref="ProtocolXml"/> é o <c>protNFe</c> serializado
/// (entra no procNFe); só vem quando há protocolo. <see cref="CancellationEvent"/> só vem na consulta
/// de NF-e cancelada (cStat 101) que trouxe o evento.
/// </summary>
public sealed record NfeSefazResult(
    int StatusCode,
    string Reason,
    string? Protocol = null,
    DateTimeOffset? ReceivedAt = null,
    string? ProtocolXml = null,
    NfeEventResult? CancellationEvent = null);

/// <summary>Pedido de cancelamento (evento 110111). <see cref="EventAt"/> em Brasília.</summary>
public sealed record NfeCancelRequest(
    string AccessKey, string AuthorizationProtocol, string Justification, string IssuerDocument, DateTimeOffset EventAt);

/// <summary>
/// Retorno de um evento: o status é o do <c>retEvento</c> (o do lote vem 128 quando processado).
/// <see cref="ProcEventXml"/> é o <c>procEventoNFe</c> (evento assinado + retorno), guardado com o documento.
/// </summary>
public sealed record NfeEventResult(
    int StatusCode,
    string Reason,
    string? Protocol = null,
    DateTimeOffset? RegisteredAt = null,
    string? ProcEventXml = null,
    string? Justification = null);

/// <summary>Sem resposta da SEFAZ (rede, tempo esgotado): a situação real é desconhecida.</summary>
public sealed class NfeCommunicationException(string message, Exception? inner = null) : Exception(message, inner);

public static class NfeStatusCodes
{
    public const int BatchProcessed = 104;
    public const int InOperation = 107;
    public const int NotFound = 217;

    /// <summary>539: o mesmo número já existe na SEFAZ com outra chave de acesso.</summary>
    public const int NumberUsedByAnotherKey = 539;

    public static bool IsAuthorized(int code) => code is 100 or 150;

    public static bool IsDenied(int code) => code is 110 or 301 or 302 or 303;

    public static bool IsDuplicate(int code) => code is 204 or 539;

    /// <summary>Consulta: NF-e cancelada (o topo é 101; o protNFe ainda traz a autorização original).</summary>
    public const int Cancelled = 101;

    /// <summary>Consulta: NF-e cancelada — 101, ou 151 (cancelamento homologado fora de prazo).</summary>
    public static bool IsCancelledConsult(int code) => code is Cancelled or 151;

    /// <summary>573: o evento já está registrado na SEFAZ — resolver pela consulta.</summary>
    public const int DuplicateEvent = 573;

    /// <summary>135: evento registrado e vinculado; 155: cancelamento homologado fora de prazo.</summary>
    public static bool IsCancellationRegistered(int code) => code is 135 or 155;
}

/// <summary>Ponto de simulação nos testes da Application. Implementação real: <see cref="ZeusNfeSefazClient"/>.</summary>
public interface INfeSefazClient
{
    Task<NfeSefazResult> AuthorizeAsync(SignedNfe nfe, NfeServiceSettings settings, CancellationToken cancellationToken = default);

    Task<NfeSefazResult> ConsultProtocolAsync(string accessKey, NfeServiceSettings settings, CancellationToken cancellationToken = default);

    Task<NfeSefazResult> ServiceStatusAsync(NfeServiceSettings settings, CancellationToken cancellationToken = default);

    Task<NfeEventResult> CancelAsync(NfeCancelRequest request, NfeServiceSettings settings, CancellationToken cancellationToken = default);
}

internal static class NfeSefazResponseMapper
{
    /// <summary>Autorização síncrona: com o lote processado (104) vale o protocolo da nota.</summary>
    public static NfeSefazResult FromAuthorization(retEnviNFe response) =>
        response.cStat == NfeStatusCodes.BatchProcessed && response.protNFe?.infProt is not null
            ? FromProtocol(response.protNFe)
            : new NfeSefazResult(response.cStat, response.xMotivo ?? string.Empty);

    /// <summary>
    /// O protocolo só vale quando o status do topo é de autorização/denegação: numa nota cancelada
    /// o topo é 101 e o protNFe ainda traz a autorização original (100) — aí vale o evento de cancelamento.
    /// </summary>
    public static NfeSefazResult FromConsult(retConsSitNFe response)
    {
        if (response.protNFe?.infProt is not null
            && (NfeStatusCodes.IsAuthorized(response.cStat) || NfeStatusCodes.IsDenied(response.cStat)))
            return FromProtocol(response.protNFe);

        var cancellation = NfeStatusCodes.IsCancelledConsult(response.cStat)
            ? response.procEventoNFe?
                .Where(e => e.evento?.infEvento?.tpEvento == NFeTipoEvento.TeNfeCancelamento
                            && e.retEvento?.infEvento is { } info && NfeStatusCodes.IsCancellationRegistered(info.cStat))
                .Select(FromProcEvent)
                .FirstOrDefault()
            : null;

        return new NfeSefazResult(response.cStat, response.xMotivo ?? string.Empty, CancellationEvent: cancellation);
    }

    /// <summary>Retorno do envio do evento: vale o <c>retEvento</c>; sem ele, o status do lote.</summary>
    public static NfeEventResult FromEvent(
        retEnvEvento response, IReadOnlyList<NFe.Classes.Servicos.Consulta.procEventoNFe>? events)
    {
        var info = response.retEvento?.FirstOrDefault()?.infEvento;

        if (info is null)
            return new NfeEventResult(response.cStat, response.xMotivo ?? string.Empty);

        var proc = info.nProt is null ? null : events?.FirstOrDefault(e => e.retEvento?.infEvento?.nProt == info.nProt);

        return proc is not null
            ? FromProcEvent(proc)
            : new NfeEventResult(info.cStat, info.xMotivo ?? string.Empty, info.nProt, RegisteredAt(info));
    }

    private static NfeEventResult FromProcEvent(NFe.Classes.Servicos.Consulta.procEventoNFe proc)
    {
        var info = proc.retEvento.infEvento;

        return new NfeEventResult(
            info.cStat, info.xMotivo ?? string.Empty, info.nProt, RegisteredAt(info),
            FuncoesXml.ClasseParaXmlString(proc), proc.evento?.infEvento?.detEvento?.xJust);
    }

    /// <summary>
    /// <c>dhRegEvento</c> com o fuso que a SEFAZ mandou (o <c>DateTime</c> da Zeus perde o fuso).
    /// ⚠️ Se o <c>ProxydhRegEvento</c> da Zeus não devolver o texto original, o teste
    /// <c>Registered_event_uses_the_event_status_not_the_batch_status</c> acusa — ajustar aqui.
    /// </summary>
    private static DateTimeOffset? RegisteredAt(infEventoRet info) =>
        DateTimeOffset.TryParse(info.ProxydhRegEvento, CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)
            ? at
            : info.dhRegEvento == default ? null : new DateTimeOffset(info.dhRegEvento);

    public static NfeSefazResult FromStatus(retConsStatServ response) =>
        new(response.cStat, response.xMotivo ?? string.Empty);

    private static NfeSefazResult FromProtocol(protNFe protocol)
    {
        var info = protocol.infProt;

        return new NfeSefazResult(
            info.cStat, info.xMotivo ?? string.Empty, info.nProt, info.dhRecbto, FuncoesXml.ClasseParaXmlString(protocol));
    }
}
