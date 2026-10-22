using SiagroB1.Fiscal.Cte;
using SiagroB1.Fiscal.Nfe;

namespace SiagroB1.Application.Tests.Support;

/// <summary>
/// SEFAZ do CT-e simulada: cada chamada consome a próxima resposta da fila (fila vazia = teste mal montado).
/// Para simular tempo esgotado, enfileire <see cref="NoResponse"/> (lança <see cref="NfeCommunicationException"/>).
/// Os contadores por método dizem quantas vezes cada serviço foi chamado.
/// </summary>
public sealed class FakeCteSefazClient : ICteSefazClient
{
    public Queue<Func<SignedCte, NfeSefazResult>> AuthorizeResponses { get; } = new();
    public Queue<Func<string, NfeSefazResult>> ConsultResponses { get; } = new();
    public Queue<Func<CteCancelRequest, NfeEventResult>> CancelResponses { get; } = new();
    public Queue<Func<CteCorrectionRequest, NfeEventResult>> CorrectionResponses { get; } = new();
    public Queue<Func<CteVoidNumberRequest, NfeVoidNumberResult>> VoidNumberResponses { get; } = new();
    public NfeSefazResult StatusResponse { get; set; } = new(107, "Serviço em Operação");

    public List<SignedCte> Sent { get; } = [];
    public List<string> Consulted { get; } = [];
    public List<CteCancelRequest> CancelRequests { get; } = [];
    public List<CteCorrectionRequest> CorrectionRequests { get; } = [];
    public List<CteVoidNumberRequest> VoidNumberRequests { get; } = [];
    public List<CteServiceSettings> Settings { get; } = [];

    public int AuthorizeCalls => Sent.Count;
    public int ConsultCalls => Consulted.Count;
    public int StatusCalls { get; private set; }
    public int CancelCalls => CancelRequests.Count;
    public int CorrectionCalls => CorrectionRequests.Count;
    public int VoidNumberCalls => VoidNumberRequests.Count;

    /// <summary>Deixa o teste olhar o banco no instante do envio.</summary>
    public Func<Task>? BeforeAuthorize { get; set; }

    public async Task<NfeSefazResult> AuthorizeAsync(
        SignedCte cte, CteServiceSettings settings, CancellationToken cancellationToken = default)
    {
        Sent.Add(cte);
        Settings.Add(settings);

        if (BeforeAuthorize is not null)
            await BeforeAuthorize();

        return AuthorizeResponses.Dequeue()(cte);
    }

    public Task<NfeSefazResult> ConsultProtocolAsync(
        string accessKey, CteServiceSettings settings, CancellationToken cancellationToken = default)
    {
        Consulted.Add(accessKey);
        Settings.Add(settings);
        return Task.FromResult(ConsultResponses.Dequeue()(accessKey));
    }

    public Task<NfeSefazResult> ServiceStatusAsync(CteServiceSettings settings, CancellationToken cancellationToken = default)
    {
        StatusCalls++;
        Settings.Add(settings);
        return Task.FromResult(StatusResponse);
    }

    public Task<NfeEventResult> CancelAsync(
        CteCancelRequest request, CteServiceSettings settings, CancellationToken cancellationToken = default)
    {
        CancelRequests.Add(request);
        Settings.Add(settings);
        return Task.FromResult(CancelResponses.Dequeue()(request));
    }

    public Task<NfeEventResult> SendCorrectionAsync(
        CteCorrectionRequest request, CteServiceSettings settings, CancellationToken cancellationToken = default)
    {
        CorrectionRequests.Add(request);
        Settings.Add(settings);
        return Task.FromResult(CorrectionResponses.Dequeue()(request));
    }

    public Task<NfeVoidNumberResult> VoidNumberAsync(
        CteVoidNumberRequest request, CteServiceSettings settings, CancellationToken cancellationToken = default)
    {
        VoidNumberRequests.Add(request);
        Settings.Add(settings);
        return Task.FromResult(VoidNumberResponses.Dequeue()(request));
    }

    // ---- Respostas prontas ----

    public const string AuthorizationProtocol = "143260000000001";
    public static readonly DateTimeOffset AuthorizedAt = new(2026, 10, 22, 10, 0, 5, TimeSpan.FromHours(-3));

    public static NfeSefazResult Authorized(string accessKey, int status = 100) => new(
        status, "Autorizado o uso do CT-e", AuthorizationProtocol, AuthorizedAt,
        $"<protCTe versao=\"4.00\" xmlns=\"http://www.portalfiscal.inf.br/cte\"><infProt><tpAmb>2</tpAmb><verAplic>TESTE</verAplic>" +
        $"<chCTe>{accessKey}</chCTe><dhRecbto>2026-10-22T10:00:05-03:00</dhRecbto><nProt>{AuthorizationProtocol}</nProt>" +
        $"<digVal>TESTE=</digVal><cStat>{status}</cStat><xMotivo>Autorizado o uso do CT-e</xMotivo></infProt></protCTe>");

    public static NfeSefazResult Rejected(int status = 209, string reason = "Rejeição: IE do emitente inválida") => new(status, reason);

    /// <summary>Tempo esgotado: a situação real na SEFAZ é desconhecida.</summary>
    public static NfeSefazResult NoResponse(object _) => throw new NfeCommunicationException("Tempo esgotado.");

    public const string EventProtocol = "143260000000099";
    public static readonly DateTimeOffset EventRegisteredAt = new(2026, 10, 22, 11, 0, 3, TimeSpan.FromHours(-3));

    public static NfeEventResult CancellationRegistered(CteCancelRequest r, int status = 135) => new(
        status, "Evento registrado e vinculado a CT-e", EventProtocol, EventRegisteredAt,
        $"<procEventoCTe versao=\"4.00\"><eventoCTe><infEvento><chCTe>{r.AccessKey}</chCTe><tpEvento>110111</tpEvento></infEvento></eventoCTe>" +
        $"<retEventoCTe><infEvento><cStat>{status}</cStat><nProt>{EventProtocol}</nProt></infEvento></retEventoCTe></procEventoCTe>",
        r.Justification);

    public static NfeEventResult CorrectionRegistered(CteCorrectionRequest r, int status = 135) => new(
        status, "Evento registrado e vinculado a CT-e", EventProtocol, EventRegisteredAt,
        $"<procEventoCTe versao=\"4.00\"><eventoCTe><infEvento><chCTe>{r.AccessKey}</chCTe><tpEvento>110110</tpEvento>" +
        $"<nSeqEvento>{r.Sequence}</nSeqEvento></infEvento></eventoCTe>" +
        $"<retEventoCTe><infEvento><cStat>{status}</cStat><nProt>{EventProtocol}</nProt></infEvento></retEventoCTe></procEventoCTe>",
        Sequence: r.Sequence);

    public const string VoidNumberProtocol = "143260000000777";

    public static NfeVoidNumberResult NumberVoided(CteVoidNumberRequest r) => new(
        102, "Inutilização de número homologado", VoidNumberProtocol,
        $"<procInutCTe versao=\"4.00\"><inutCTe><infInut><serie>{r.Series}</serie><nCTIni>{r.From}</nCTIni><nCTFin>{r.To}</nCTFin></infInut></inutCTe>" +
        $"<retInutCTe><infInut><cStat>102</cStat><nProt>{VoidNumberProtocol}</nProt></infInut></retInutCTe></procInutCTe>");
}
