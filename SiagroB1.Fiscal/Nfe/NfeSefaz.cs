using DFe.Utils;
using NFe.Classes.Protocolo;
using NFe.Classes.Servicos.Consulta;
using NFe.Classes.Servicos.Recepcao;
using NFe.Classes.Servicos.Status;

namespace SiagroB1.Fiscal.Nfe;

/// <summary>
/// Resposta da SEFAZ sem tipos da Zeus. <see cref="ProtocolXml"/> é o <c>protNFe</c> serializado
/// (entra no procNFe); só vem quando há protocolo.
/// </summary>
public sealed record NfeSefazResult(
    int StatusCode,
    string Reason,
    string? Protocol = null,
    DateTimeOffset? ReceivedAt = null,
    string? ProtocolXml = null);

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
}

/// <summary>Ponto de simulação nos testes da Application. Implementação real: <see cref="ZeusNfeSefazClient"/>.</summary>
public interface INfeSefazClient
{
    Task<NfeSefazResult> AuthorizeAsync(SignedNfe nfe, NfeServiceSettings settings, CancellationToken cancellationToken = default);

    Task<NfeSefazResult> ConsultProtocolAsync(string accessKey, NfeServiceSettings settings, CancellationToken cancellationToken = default);

    Task<NfeSefazResult> ServiceStatusAsync(NfeServiceSettings settings, CancellationToken cancellationToken = default);
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
    /// o topo é 101 e o protNFe ainda traz a autorização original (100).
    /// </summary>
    public static NfeSefazResult FromConsult(retConsSitNFe response) =>
        response.protNFe?.infProt is not null
        && (NfeStatusCodes.IsAuthorized(response.cStat) || NfeStatusCodes.IsDenied(response.cStat))
            ? FromProtocol(response.protNFe)
            : new NfeSefazResult(response.cStat, response.xMotivo ?? string.Empty);

    public static NfeSefazResult FromStatus(retConsStatServ response) =>
        new(response.cStat, response.xMotivo ?? string.Empty);

    private static NfeSefazResult FromProtocol(protNFe protocol)
    {
        var info = protocol.infProt;

        return new NfeSefazResult(
            info.cStat, info.xMotivo ?? string.Empty, info.nProt, info.dhRecbto, FuncoesXml.ClasseParaXmlString(protocol));
    }
}
