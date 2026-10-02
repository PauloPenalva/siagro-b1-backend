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
    public Queue<Func<string, NfeSefazResult>> AuthorizeResponses { get; } = new();
    public Queue<Func<string, NfeSefazResult>> ConsultResponses { get; } = new();
    public NfeSefazResult StatusResponse { get; set; } = new(107, "Serviço em Operação");
    public List<SignedNfe> Sent { get; } = [];
    public List<string> Consulted { get; } = [];
    public Func<Task>? BeforeAuthorize { get; set; }

    public async Task<NfeSefazResult> AuthorizeAsync(
        SignedNfe nfe, NfeServiceSettings settings, CancellationToken cancellationToken = default)
    {
        Sent.Add(nfe);

        if (BeforeAuthorize is not null)
            await BeforeAuthorize();

        return AuthorizeResponses.Dequeue()(nfe.AccessKey);
    }

    public Task<NfeSefazResult> ConsultProtocolAsync(
        string accessKey, NfeServiceSettings settings, CancellationToken cancellationToken = default)
    {
        Consulted.Add(accessKey);
        return Task.FromResult(ConsultResponses.Dequeue()(accessKey));
    }

    public Task<NfeSefazResult> ServiceStatusAsync(NfeServiceSettings settings, CancellationToken cancellationToken = default) =>
        Task.FromResult(StatusResponse);

    public static NfeSefazResult Authorized(string accessKey, int status = 100) => new(
        status, "Autorizado o uso da NF-e", "135260000000001", new DateTimeOffset(2026, 10, 2, 10, 0, 5, TimeSpan.FromHours(-3)),
        FuncoesXml.ClasseParaXmlString(new protNFe
        {
            versao = "4.00",
            infProt = new infProt
            {
                Id = "ID135260000000001", tpAmb = TipoAmbiente.Homologacao, chNFe = accessKey, cStat = status,
                xMotivo = "Autorizado o uso da NF-e",
                nProt = "135260000000001", dhRecbto = new DateTimeOffset(2026, 10, 2, 10, 0, 5, TimeSpan.FromHours(-3)),
                digVal = "abc=", verAplic = "TESTE",
            },
        }));

    public static NfeSefazResult Rejected(int status = 209, string reason = "Rejeição: IE do emitente inválida") => new(status, reason);

    public static NfeSefazResult NoResponse(string _) => throw new NfeCommunicationException("Tempo esgotado.");
}
