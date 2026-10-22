using System.Net;
using System.Net.Sockets;
using System.Xml;
using CTe.Classes;
using CTe.Classes.Servicos.Evento;
using CTe.Classes.Servicos.Evento.Flags;
using CTe.Classes.Servicos.Tipos;
using CTe.Servicos.Enderecos.Helpers;
using CTe.Servicos.Eventos;
using CTe.Servicos.Factory;
using CTe.Servicos.Inutilizacao;
using CTe.Utils.Evento;
using CTe.Utils.Extensoes;
using CTe.Utils.Validacao;
using DFe.Classes.Entidades;
using DFe.Classes.Flags;
using DFe.Utils;
using DFe.Utils.Assinatura;
using SiagroB1.Fiscal.Nfe;

namespace SiagroB1.Fiscal.Cte;

/// <summary>
/// SEFAZ do CT-e 4.00 pela Zeus, no nível dos WSDLs: os serviços prontos da Zeus releem o certificado
/// do disco/repositório e reassinam o CT-e, então aqui os pedidos são montados, assinados com o
/// <see cref="CteServiceSettings.Certificate"/> já aberto e enviados por <see cref="WsdlFactory"/>
/// (que aceita o certificado). O CT-e vai com o XML assinado verbatim — nunca re-serializado.
/// As chamadas da biblioteca são síncronas: rodam fora da thread da requisição.
/// </summary>
public sealed class ZeusCteSefazClient : ICteSefazClient
{
    public Task<NfeSefazResult> AuthorizeAsync(
        SignedCte cte, CteServiceSettings settings, CancellationToken cancellationToken = default) =>
        RunAsync(settings, (configuration, certificate) =>
        {
            var request = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
            var xml = cte.Xml;

            // Mesmo ajuste da Zeus (ExtCTe.CriaRequestWs): PR e MT exigem o namespace explícito no <CTe>.
            if (configuration.cUF is Estado.PR or Estado.MT)
                xml = xml.Replace("<CTe>", $"<CTe xmlns=\"{CteSefazResponseMapper.Namespace}\">", StringComparison.Ordinal);

            request.LoadXml(xml);
            var response = WsdlFactory.CriaWsdlCteRecepcaoSincronoV4(configuration, certificate).CTeRecepcaoSincV4(request);

            return CteSefazResponseMapper.FromAuthorization(response.OuterXml);
        }, cancellationToken);

    public Task<NfeSefazResult> ConsultProtocolAsync(
        string accessKey, CteServiceSettings settings, CancellationToken cancellationToken = default) =>
        RunAsync(settings, (configuration, certificate) =>
        {
            var request = ClassesFactory.CriarconsSitCTe(accessKey, configuration);
            Validate(() => request.ValidarSchema(configuration));

            var response = WsdlFactory.CriaWsdlConsultaProtocoloV4(configuration, certificate).cteConsultaCT(request.CriaRequestWs());
            return CteSefazResponseMapper.FromConsult(response.OuterXml);
        }, cancellationToken);

    public Task<NfeSefazResult> ServiceStatusAsync(
        CteServiceSettings settings, CancellationToken cancellationToken = default) =>
        RunAsync(settings, (configuration, certificate) =>
        {
            var request = ClassesFactory.CriaConsStatServCTe(configuration);
            Validate(() => request.ValidarSchema(configuration));

            var response = WsdlFactory.CriaWsdlCteStatusServico(configuration, certificate).cteStatusServicoCT(request.CriaRequestWs());
            return CteSefazResponseMapper.FromStatus(response.OuterXml);
        }, cancellationToken);

    public Task<NfeEventResult> CancelAsync(
        CteCancelRequest request, CteServiceSettings settings, CancellationToken cancellationToken = default) =>
        RunAsync(settings, (configuration, certificate) =>
        {
            // Cancelamento é evento único por CT-e (nSeqEvento 1).
            var container = ClassesFactory.CriaEvCancCTe(request.Justification, request.Protocol);
            var result = SendEvent(CTeTipoEvento.Cancelamento, 1, request.AccessKey, request.IssuerCnpj, request.EventAt,
                container, configuration, certificate);

            return result with { Justification = result.Justification ?? request.Justification };
        }, cancellationToken);

    public Task<NfeEventResult> SendCorrectionAsync(
        CteCorrectionRequest request, CteServiceSettings settings, CancellationToken cancellationToken = default) =>
        RunAsync(settings, (configuration, certificate) =>
        {
            var container = ClassesFactory.CriaEvCCeCTe(request.Items
                .Select(i => new infCorrecao
                {
                    grupoAlterado = i.Group,
                    campoAlterado = i.Field,
                    valorAlterado = i.Value,
                    nroItemAlterado = i.GroupNumber,
                })
                .ToList());

            var result = SendEvent(CTeTipoEvento.CartaCorrecao, request.Sequence, request.AccessKey, request.IssuerCnpj,
                request.EventAt, container, configuration, certificate);

            return result with { Sequence = result.Sequence ?? request.Sequence };
        }, cancellationToken);

    public Task<NfeVoidNumberResult> VoidNumberAsync(
        CteVoidNumberRequest request, CteServiceSettings settings, CancellationToken cancellationToken = default) =>
        RunAsync(settings, (configuration, certificate) =>
        {
            // ⚠️ A Zeus não tem endereço de inutilização para o leiaute 4.00 (UrlHelper só o define até a 3.00):
            // sem URL, recusa antes de assinar em vez de falhar no transporte.
            if (string.IsNullOrWhiteSpace(UrlHelper.ObterUrlServico(configuration).CteInutilizacao))
                throw new NotSupportedException(
                    "A SEFAZ não oferece o serviço de inutilização de numeração do CT-e 4.00 para esta UF.");

            // A UF da faixa é a do pedido (o Id usa o cUF da configuração).
            configuration.cUF = (Estado)int.Parse(request.StateCode);

            var inut = ClassesFactory.CriaInutCTe(
                new ConfigInutiliza(request.IssuerCnpj, (short)request.Series, request.From, request.To, request.Year % 100,
                    request.Justification),
                configuration);

            var defaults = new ConfiguracaoCertificado();
            inut.Signature = AssinaturaDigital.Assina(
                inut, inut.infInut.Id, certificate, defaults.SignatureMethodSignedXml, defaults.DigestMethodReference, false);

            // A Zeus só valida inutCTe até a 3.00: o XSD 4.00 é chamado direto.
            var requestXml = inut.ObterXmlString();
            Validate(() => Validador.Valida(requestXml, "inutCTe_v4.00.xsd", configuration));

            var response = WsdlFactory.CriaWsdlCteInutilizacao(configuration, certificate).cteInutilizacaoCT(inut.CriaRequestWs());
            return CteSefazResponseMapper.FromVoidNumber(requestXml, response.OuterXml);
        }, cancellationToken);

    private static NfeEventResult SendEvent(
        CTeTipoEvento type, int sequence, string accessKey, string issuerCnpj, DateTimeOffset eventAt,
        EventoContainer container, ConfiguracaoServico configuration,
        System.Security.Cryptography.X509Certificates.X509Certificate2 certificate)
    {
        var evento = FactoryEvento.CriaEvento(type, sequence, accessKey, issuerCnpj, container, configuration);
        evento.infEvento.dhEvento = eventAt;

        var defaults = new ConfiguracaoCertificado();
        evento.Signature = AssinaturaDigital.Assina(
            evento, evento.infEvento.Id, certificate, defaults.SignatureMethodSignedXml, defaults.DigestMethodReference, false);
        Validate(() => evento.ValidarSchema(configuration));

        // O XML enviado é guardado verbatim no procEventoCTe (o assinado, não uma nova serialização).
        var sentXml = evento.ObterXmlString();
        var request = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
        request.LoadXml(sentXml);

        var response = WsdlFactory.CriaWsdlCteEventoV4(configuration, certificate).cteRecepcaoEvento(request);
        return CteSefazResponseMapper.FromEvent(sentXml, response.OuterXml);
    }

    /// <summary>A Zeus CT-e lança Exception crua ("Erros da validação : ...") na falha de XSD.</summary>
    private static void Validate(Action validate)
    {
        try
        {
            validate();
        }
        catch (Exception e) when (e.Message.StartsWith("Erros da valida", StringComparison.Ordinal))
        {
            throw new NfeValidationException(e.Message);
        }
    }

    private static Task<T> RunAsync<T>(
        CteServiceSettings settings,
        Func<ConfiguracaoServico, System.Security.Cryptography.X509Certificates.X509Certificate2, T> call,
        CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            // A serialização da Zeus CT-e lê o leiaute do singleton global.
            CteZeusConfiguration.PinGlobalLayout();

            try
            {
                return call(CteZeusConfiguration.Create(settings), settings.Certificate);
            }
            catch (Exception e) when (e is WebException or HttpRequestException or TimeoutException or SocketException
                                          or (IOException and not (FileNotFoundException or DirectoryNotFoundException))
                                          or TaskCanceledException)
            {
                throw new NfeCommunicationException(e.Message, e);
            }
        }, cancellationToken);
}
