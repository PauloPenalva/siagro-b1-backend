using System.Net;
using System.Net.Sockets;
using DFe.Classes.Flags;
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

    public Task<NfeEventResult> SendCorrectionAsync(
        NfeCorrectionRequest request, NfeServiceSettings settings, CancellationToken cancellationToken = default) =>
        RunAsync(settings, services =>
        {
            // Lote de um evento só (idLote 1); a sequência é a da carta (1–20).
            var response = services.RecepcaoEventoCartaCorrecao(
                1, request.Sequence, request.AccessKey, request.Text, request.IssuerDocument, request.EventAt);

            var result = NfeSefazResponseMapper.FromEvent(response.Retorno, response.ProcEventosNFe);

            // Sem o procEvento casado, o mapper não sabe a sequência nem o texto: valem os do pedido.
            return result with
            {
                Sequence = result.Sequence ?? request.Sequence,
                CorrectionText = result.CorrectionText ?? request.Text,
            };
        }, cancellationToken);

    public Task<NfeVoidNumberResult> VoidNumberAsync(
        NfeVoidNumberRequest request, NfeServiceSettings settings, CancellationToken cancellationToken = default) =>
        RunAsync(settings, services =>
        {
            // UF, ambiente e modelo vêm da configuração; a Zeus monta, assina e valida no XSD.
            var response = services.NfeInutilizacao(
                request.TaxId, request.Year, ModeloDocumento.NFe, request.Series, request.Number, request.Number,
                request.Justification);

            // ⚠️ EnvioStr = o inutNFe assinado que foi enviado (vira o procInutNFe). Se o E2E mostrar que é o
            // envelope SOAP, extraia o nó <inutNFe> antes de passar — o mapper só perde o comprovante, não a homologação.
            return NfeSefazResponseMapper.FromVoidNumber(response.EnvioStr, response.Retorno);
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
