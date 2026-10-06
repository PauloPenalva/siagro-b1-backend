using System.Net;
using System.Net.Sockets;
using DFe.Wsdl.Common;
using NFe.Classes.Servicos.Tipos;
using NFe.Servicos;
using NFe.Utils.Excecoes;

namespace SiagroB1.Fiscal.Nfe;

/// <summary>
/// SEFAZ pela Zeus. As chamadas da biblioteca são síncronas (HttpWebRequest): rodam fora da
/// thread da requisição. Falha de transporte vira <see cref="NfeCommunicationException"/> — quem
/// chama trata como "sem resposta" e mantém o documento em processamento.
/// </summary>
public sealed class ZeusNfeSefazClient : INfeSefazClient
{
    public Task<NfeSefazResult> AuthorizeAsync(
        SignedNfe nfe, NfeServiceSettings settings, CancellationToken cancellationToken = default) =>
        RunAsync(settings, services =>
        {
            // O lote leva uma NF-e só; o número da nota (9 dígitos) cabe no idLote.
            var batchId = int.Parse(nfe.AccessKey.Substring(25, 9));
            var response = services.NFeAutorizacao(batchId, IndicadorSincronizacao.Sincrono, [nfe.Document]);

            return NfeSefazResponseMapper.FromAuthorization(response.Retorno);
        }, cancellationToken);

    public Task<NfeSefazResult> ConsultProtocolAsync(
        string accessKey, NfeServiceSettings settings, CancellationToken cancellationToken = default) =>
        RunAsync(settings, services => NfeSefazResponseMapper.FromConsult(services.NfeConsultaProtocolo(accessKey).Retorno),
            cancellationToken);

    public Task<NfeSefazResult> ServiceStatusAsync(
        NfeServiceSettings settings, CancellationToken cancellationToken = default) =>
        RunAsync(settings, services => NfeSefazResponseMapper.FromStatus(services.NfeStatusServico().Retorno),
            cancellationToken);

    public Task<NfeEventResult> CancelAsync(
        NfeCancelRequest request, NfeServiceSettings settings, CancellationToken cancellationToken = default) =>
        RunAsync(settings, services =>
        {
            // Lote de um evento só; cancelamento é evento único por NF-e (nSeqEvento 1).
            var response = services.RecepcaoEventoCancelamento(
                1, 1, request.AuthorizationProtocol, request.AccessKey, request.Justification, request.IssuerDocument,
                request.EventAt);

            return NfeSefazResponseMapper.FromEvent(response.Retorno, response.ProcEventosNFe);
        }, cancellationToken);

    private static Task<T> RunAsync<T>(
        NfeServiceSettings settings, Func<ServicosNFe, T> call, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            // Estático da biblioteca (caminho .NET Core): mesma escolha da configuração.
            ConfiguracaoServicoWSDL.ValidarCertificadoDoServidorNetCore = settings.ValidateServerCertificate;

            try
            {
                using var services = new ServicosNFe(NfeZeusConfiguration.Create(settings), settings.Certificate);
                return call(services);
            }
            catch (Exception e) when (e is ComunicacaoException or WebException or HttpRequestException
                                          or TimeoutException or SocketException
                                          or (IOException and not (FileNotFoundException or DirectoryNotFoundException))
                                          or TaskCanceledException)
            {
                throw new NfeCommunicationException(e.Message, e);
            }
        }, cancellationToken);
}
