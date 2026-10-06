using DFe.Classes.Flags;
using DFe.Utils;
using NFe.Classes.Protocolo;
using SiagroB1.Fiscal.Nfe;

namespace SiagroB1.Application.Tests.Support;

/// <summary>
/// SEFAZ simulada: cada chamada consome a próxima resposta da fila (recebe a chave de acesso).
/// <see cref="BeforeAuthorize"/> deixa o teste olhar o banco no instante do envio.
/// </summary>
public sealed class FakeNfeSefazClient : INfeSefazClient
{
    /// <summary>Marcador de digest: o fake troca pelo digest do XML realmente enviado.</summary>
    public const string MatchingDigest = "__MATCH__";

    public Queue<Func<string, NfeSefazResult>> AuthorizeResponses { get; } = new();
    public Queue<Func<string, NfeSefazResult>> ConsultResponses { get; } = new();
    public NfeSefazResult StatusResponse { get; set; } = new(107, "Serviço em Operação");
    public List<SignedNfe> Sent { get; } = [];
    public List<string> Consulted { get; } = [];
    public List<NfeServiceSettings> ConsultedSettings { get; } = [];
    public Func<Task>? BeforeAuthorize { get; set; }

    public async Task<NfeSefazResult> AuthorizeAsync(
        SignedNfe nfe, NfeServiceSettings settings, CancellationToken cancellationToken = default)
    {
        Sent.Add(nfe);

        if (BeforeAuthorize is not null)
            await BeforeAuthorize();

        var result = AuthorizeResponses.Dequeue()(nfe.AccessKey);
        return result.ProtocolXml is not null && result.ProtocolXml.Contains(MatchingDigest)
            ? result with { ProtocolXml = result.ProtocolXml.Replace(MatchingDigest, NfeProcComposer.SignedDigest(nfe.Xml)) }
            : result;
    }

    public Task<NfeSefazResult> ConsultProtocolAsync(
        string accessKey, NfeServiceSettings settings, CancellationToken cancellationToken = default)
    {
        Consulted.Add(accessKey);
        ConsultedSettings.Add(settings);
        var result = ConsultResponses.Dequeue()(accessKey);

        if (result.ProtocolXml is not null && result.ProtocolXml.Contains(MatchingDigest) && Sent.Count > 0)
            result = result with { ProtocolXml = result.ProtocolXml.Replace(MatchingDigest, NfeProcComposer.SignedDigest(Sent[^1].Xml)) };

        return Task.FromResult(result);
    }

    public Task<NfeSefazResult> ServiceStatusAsync(NfeServiceSettings settings, CancellationToken cancellationToken = default) =>
        Task.FromResult(StatusResponse);

    public Queue<Func<NfeCancelRequest, NfeEventResult>> CancelResponses { get; } = new();
    public List<NfeCancelRequest> CancelRequests { get; } = [];
    public List<NfeServiceSettings> CancelSettings { get; } = [];

    public Task<NfeEventResult> CancelAsync(
        NfeCancelRequest request, NfeServiceSettings settings, CancellationToken cancellationToken = default)
    {
        CancelRequests.Add(request);
        CancelSettings.Add(settings);
        return Task.FromResult(CancelResponses.Dequeue()(request));
    }

    public Queue<Func<NfeCorrectionRequest, NfeEventResult>> CorrectionResponses { get; } = new();
    public List<NfeCorrectionRequest> CorrectionRequests { get; } = [];
    public List<NfeServiceSettings> CorrectionSettings { get; } = [];

    public Task<NfeEventResult> SendCorrectionAsync(
        NfeCorrectionRequest request, NfeServiceSettings settings, CancellationToken cancellationToken = default)
    {
        CorrectionRequests.Add(request);
        CorrectionSettings.Add(settings);
        return Task.FromResult(CorrectionResponses.Dequeue()(request));
    }

    public static string CorrectionProtocol(int sequence) => $"1352600000002{sequence:D2}";

    public static readonly DateTimeOffset CorrectionRegisteredAt = new(2026, 10, 6, 9, 15, 0, TimeSpan.FromHours(-3));

    public static NfeEventResult CorrectionRegistered(int sequence, string text, int status = 135) => new(
        status, "Evento registrado e vinculado a NF-e", CorrectionProtocol(sequence), CorrectionRegisteredAt,
        $"<procEventoNFe versao=\"1.00\"><evento><infEvento><tpEvento>110110</tpEvento><nSeqEvento>{sequence}</nSeqEvento>" +
        $"<detEvento><xCorrecao>{text}</xCorrecao></detEvento></infEvento></evento>" +
        $"<retEvento><infEvento><cStat>{status}</cStat><nProt>{CorrectionProtocol(sequence)}</nProt></infEvento></retEvento></procEventoNFe>",
        Sequence: sequence, CorrectionText: text);

    public const string CancellationProtocol = "135260000000099";

    public static readonly DateTimeOffset CancelledAt = new(2026, 10, 2, 15, 30, 0, TimeSpan.FromHours(-3));

    public static NfeEventResult CancellationRegistered(string accessKey, int status = 135) => new(
        status, "Evento registrado e vinculado a NF-e", CancellationProtocol, CancelledAt,
        $"<procEventoNFe versao=\"1.00\"><evento><infEvento><chNFe>{accessKey}</chNFe><tpEvento>110111</tpEvento></infEvento></evento>" +
        $"<retEvento><infEvento><cStat>{status}</cStat><nProt>{CancellationProtocol}</nProt></infEvento></retEvento></procEventoNFe>");

    public static NfeSefazResult ConsultCancelled(string accessKey, bool withEvent = true, int status = 101) => new(
        status, status == 151 ? "Cancelamento de NF-e homologado fora de prazo" : "Cancelamento de NF-e homologado",
        CancellationEvent: withEvent
            ? CancellationRegistered(accessKey) with { Justification = "Cancelada direto no portal da SEFAZ" }
            : null);

    public static NfeSefazResult Authorized(string accessKey, int status = 100, string digVal = MatchingDigest) => new(
        status, "Autorizado o uso da NF-e", "135260000000001", new DateTimeOffset(2026, 10, 2, 10, 0, 5, TimeSpan.FromHours(-3)),
        FuncoesXml.ClasseParaXmlString(new protNFe
        {
            versao = "4.00",
            infProt = new infProt
            {
                Id = "ID135260000000001", tpAmb = TipoAmbiente.Homologacao, chNFe = accessKey, cStat = status,
                xMotivo = "Autorizado o uso da NF-e",
                nProt = "135260000000001", dhRecbto = new DateTimeOffset(2026, 10, 2, 10, 0, 5, TimeSpan.FromHours(-3)),
                digVal = digVal, verAplic = "TESTE",
            },
        }));

    public static NfeSefazResult Rejected(int status = 209, string reason = "Rejeição: IE do emitente inválida") => new(status, reason);

    public static NfeSefazResult NoResponse(string _) => throw new NfeCommunicationException("Tempo esgotado.");
}
