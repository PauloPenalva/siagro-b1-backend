using SiagroB1.Fiscal.Mdfe;
using SiagroB1.Fiscal.Nfe;

namespace SiagroB1.Application.Tests.Support;

/// <summary>
/// SEFAZ do MDF-e simulada: cada chamada consome a próxima resposta da fila (fila vazia = teste mal montado).
/// Para simular tempo esgotado, enfileire <see cref="NoResponse"/> (lança <see cref="NfeCommunicationException"/>).
/// Os contadores por método dizem quantas vezes cada serviço foi chamado.
/// </summary>
public sealed class FakeMdfeSefazClient : IMdfeSefazClient
{
    public Queue<Func<SignedMdfe, NfeSefazResult>> AuthorizeResponses { get; } = new();
    public Queue<Func<string, NfeSefazResult>> ConsultResponses { get; } = new();
    public Queue<Func<MdfeCloseRequest, NfeEventResult>> CloseResponses { get; } = new();
    public Queue<Func<MdfeCancelRequest, NfeEventResult>> CancelResponses { get; } = new();
    public Queue<Func<MdfeIncludeDriverRequest, NfeEventResult>> IncludeDriverResponses { get; } = new();
    public Queue<Func<string, IReadOnlyList<MdfeNotClosed>>> NotClosedResponses { get; } = new();
    public NfeSefazResult StatusResponse { get; set; } = new(107, "Serviço em Operação");

    public List<SignedMdfe> Sent { get; } = [];
    public List<string> Consulted { get; } = [];
    public List<MdfeCloseRequest> CloseRequests { get; } = [];
    public List<MdfeCancelRequest> CancelRequests { get; } = [];
    public List<MdfeIncludeDriverRequest> IncludeDriverRequests { get; } = [];
    public List<string> NotClosedQueries { get; } = [];
    public List<MdfeServiceSettings> Settings { get; } = [];

    public int AuthorizeCalls => Sent.Count;
    public int ConsultCalls => Consulted.Count;
    public int StatusCalls { get; private set; }
    public int CloseCalls => CloseRequests.Count;
    public int CancelCalls => CancelRequests.Count;
    public int IncludeDriverCalls => IncludeDriverRequests.Count;
    public int NotClosedCalls => NotClosedQueries.Count;

    /// <summary>Deixa o teste olhar o banco no instante do envio.</summary>
    public Func<Task>? BeforeAuthorize { get; set; }

    public async Task<NfeSefazResult> AuthorizeAsync(
        SignedMdfe mdfe, MdfeServiceSettings settings, CancellationToken cancellationToken = default)
    {
        Sent.Add(mdfe);
        Settings.Add(settings);

        if (BeforeAuthorize is not null)
            await BeforeAuthorize();

        return AuthorizeResponses.Dequeue()(mdfe);
    }

    public Task<NfeSefazResult> ConsultProtocolAsync(
        string accessKey, MdfeServiceSettings settings, CancellationToken cancellationToken = default)
    {
        Consulted.Add(accessKey);
        Settings.Add(settings);
        return Task.FromResult(ConsultResponses.Dequeue()(accessKey));
    }

    public Task<NfeSefazResult> ServiceStatusAsync(MdfeServiceSettings settings, CancellationToken cancellationToken = default)
    {
        StatusCalls++;
        Settings.Add(settings);
        return Task.FromResult(StatusResponse);
    }

    public Task<NfeEventResult> CloseAsync(
        MdfeCloseRequest request, MdfeServiceSettings settings, CancellationToken cancellationToken = default)
    {
        CloseRequests.Add(request);
        Settings.Add(settings);
        return Task.FromResult(CloseResponses.Dequeue()(request));
    }

    public Task<NfeEventResult> CancelAsync(
        MdfeCancelRequest request, MdfeServiceSettings settings, CancellationToken cancellationToken = default)
    {
        CancelRequests.Add(request);
        Settings.Add(settings);
        return Task.FromResult(CancelResponses.Dequeue()(request));
    }

    public Task<NfeEventResult> IncludeDriverAsync(
        MdfeIncludeDriverRequest request, MdfeServiceSettings settings, CancellationToken cancellationToken = default)
    {
        IncludeDriverRequests.Add(request);
        Settings.Add(settings);
        return Task.FromResult(IncludeDriverResponses.Dequeue()(request));
    }

    public Task<IReadOnlyList<MdfeNotClosed>> ListNotClosedAsync(
        string issuerCnpj, MdfeServiceSettings settings, CancellationToken cancellationToken = default)
    {
        NotClosedQueries.Add(issuerCnpj);
        Settings.Add(settings);
        return Task.FromResult(NotClosedResponses.Dequeue()(issuerCnpj));
    }

    // ---- Respostas prontas ----

    private const string Ns = "http://www.portalfiscal.inf.br/mdfe";

    public const string AuthorizationProtocol = "943260000000001";
    public static readonly DateTimeOffset AuthorizedAt = new(2026, 10, 23, 10, 0, 5, TimeSpan.FromHours(-3));

    public static NfeSefazResult Authorized(string accessKey, int status = 100) => new(
        status, "Autorizado o uso do MDF-e", AuthorizationProtocol, AuthorizedAt,
        $"<protMDFe versao=\"3.00\" xmlns=\"{Ns}\"><infProt><tpAmb>2</tpAmb><verAplic>TESTE</verAplic>" +
        $"<chMDFe>{accessKey}</chMDFe><dhRecbto>2026-10-23T10:00:05-03:00</dhRecbto><nProt>{AuthorizationProtocol}</nProt>" +
        $"<digVal>TESTE=</digVal><cStat>{status}</cStat><xMotivo>Autorizado o uso do MDF-e</xMotivo></infProt></protMDFe>");

    public static NfeSefazResult Rejected(int status = 611, string reason = "Rejeição: Existe MDF-e não encerrado") => new(status, reason);

    /// <summary>Tempo esgotado: a situação real na SEFAZ é desconhecida.</summary>
    public static NfeSefazResult NoResponse(object _) => throw new NfeCommunicationException("Tempo esgotado.");

    /// <summary>Tempo esgotado num evento.</summary>
    public static NfeEventResult NoEventResponse(object _) => throw new NfeCommunicationException("Tempo esgotado.");

    public const string EventProtocol = "943260000000099";
    public static readonly DateTimeOffset EventRegisteredAt = new(2026, 10, 23, 11, 0, 3, TimeSpan.FromHours(-3));

    private static NfeEventResult Registered(string key, int type, int sequence, int status, string? justification = null) => new(
        status, "Evento registrado e vinculado a MDF-e", EventProtocol, EventRegisteredAt,
        $"<procEventoMDFe versao=\"3.00\" xmlns=\"{Ns}\"><eventoMDFe><infEvento><chMDFe>{key}</chMDFe><tpEvento>{type}</tpEvento>" +
        $"<nSeqEvento>{sequence}</nSeqEvento></infEvento></eventoMDFe>" +
        $"<retEventoMDFe><infEvento><cStat>{status}</cStat><nProt>{EventProtocol}</nProt></infEvento></retEventoMDFe></procEventoMDFe>",
        justification, sequence);

    public static NfeEventResult ClosingRegistered(MdfeCloseRequest r, int status = 135) => Registered(r.AccessKey, 110112, 1, status);

    public static NfeEventResult CancellationRegistered(MdfeCancelRequest r, int status = 135) =>
        Registered(r.AccessKey, 110111, 1, status, r.Justification);

    public static NfeEventResult DriverIncluded(MdfeIncludeDriverRequest r, int status = 135) =>
        Registered(r.AccessKey, 110114, r.Sequence, status);

    public static NfeEventResult EventRejected(int status = 631, string reason = "Rejeição: Duplicidade de evento") => new(status, reason);
}
