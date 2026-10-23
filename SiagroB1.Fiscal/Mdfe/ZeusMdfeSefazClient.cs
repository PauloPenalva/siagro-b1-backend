using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Xml;
using CTe.CTeOSDocumento.Common;
using DFe.Classes.Entidades;
using DFe.Classes.Extensoes;
using DFe.Utils.Assinatura;
using MDFe.Classes.Extensoes;
using MDFe.Classes.Informacoes.Evento;
using MDFe.Classes.Informacoes.Evento.CorpoEvento;
using MDFe.Classes.Informacoes.Evento.Flags;
using MDFe.Servicos.Enderecos.Helper;
using MDFe.Servicos.Factory;
using MDFe.Utils.Configuracoes;
using MDFe.Wsdl.Gerado.MDFeStatusServico;
using MDFe.Wsdl.MDFeConsultaNaoEncerrados;
using MDFe.Wsdl.MDFeConsultaProtoloco;
using MDFe.Wsdl.MDFeEventos;
using MDFe.Wsdl.MDFeRecepcao.Sincrono;
using SiagroB1.Fiscal.Nfe;
using MdfeVersao = MDFe.Utils.Flags.VersaoServico;

namespace SiagroB1.Fiscal.Mdfe;

/// <summary>
/// SEFAZ do MDF-e 3.00 pela Zeus, no nível dos WSDLs: os serviços prontos da Zeus (e o <c>WsdlFactory</c>)
/// releem o certificado de <c>MDFeConfiguracao</c> e reassinam o MDF-e, então aqui os clientes WSDL são
/// montados com o <see cref="MdfeServiceSettings.Certificate"/> já aberto e os eventos assinados com
/// <see cref="AssinaturaDigital"/>. O MDF-e vai com o XML assinado verbatim — nunca re-serializado.
/// As chamadas da biblioteca são síncronas: rodam fora da thread da requisição.
/// </summary>
public sealed class ZeusMdfeSefazClient : IMdfeSefazClient
{
    private const string LayoutVersion = "3.00";

    public Task<NfeSefazResult> AuthorizeAsync(
        SignedMdfe mdfe, MdfeServiceSettings settings, CancellationToken cancellationToken = default) =>
        RunAsync(settings, (configuration, certificate) =>
        {
            var request = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
            request.LoadXml(mdfe.Xml);

            var url = UrlHelper.ObterUrlServico(configuration.VersaoWebService.TipoAmbiente).MDFeRecepcaoSinc;
            var response = new MDFeRecepcaoSinc(Wsdl(url, configuration, certificate)).mdfeRecepcao(request);
            return MdfeSefazResponseMapper.FromAuthorization(response.OuterXml);
        }, cancellationToken);

    public Task<NfeSefazResult> ConsultProtocolAsync(
        string accessKey, MdfeServiceSettings settings, CancellationToken cancellationToken = default) =>
        RunAsync(settings, (configuration, certificate) =>
        {
            var request = ClassesFactory.CriarConsSitMDFe(accessKey, configuration);
            Validate(() => request.ValidarSchema(configuration));

            var url = UrlHelper.ObterUrlServico(configuration.VersaoWebService.TipoAmbiente).MDFeConsulta;
            var response = new MDFeConsulta(Wsdl(url, configuration, certificate)).mdfeConsultaMDF(request.CriaRequestWs());
            return MdfeSefazResponseMapper.FromConsult(response.OuterXml);
        }, cancellationToken);

    public Task<NfeSefazResult> ServiceStatusAsync(
        MdfeServiceSettings settings, CancellationToken cancellationToken = default) =>
        RunAsync(settings, (configuration, certificate) =>
        {
            var request = ClassesFactory.CriaConsStatServMDFe(configuration);
            Validate(() => request.ValidarSchema(configuration));

            var url = UrlHelper.ObterUrlServico(configuration.VersaoWebService.TipoAmbiente).MDFeStatusServico;
            var response = new MDFeStatusServico(Wsdl(url, configuration, certificate)).mdfeStatusServicoMDF(request.CriaRequestWs());
            return MdfeSefazResponseMapper.FromStatus(response.OuterXml);
        }, cancellationToken);

    public Task<NfeEventResult> CloseAsync(
        MdfeCloseRequest request, MdfeServiceSettings settings, CancellationToken cancellationToken = default) =>
        RunAsync(settings, (configuration, certificate) =>
        {
            var container = new MDFeEvEncMDFe
            {
                DescEvento = "Encerramento",
                NProt = request.Protocol,
                DtEnc = request.ClosedOn.ToDateTime(TimeOnly.MinValue),
                CUF = (Estado)int.Parse(request.UfCode),
                CMun = long.Parse(request.CityIbge),
            };

            // Encerramento é evento único por MDF-e (nSeqEvento 1).
            return SendEvent(MDFeTipoEvento.Encerramento, 1, request.AccessKey, request.IssuerCnpj, request.EventAt,
                container, configuration, certificate);
        }, cancellationToken);

    public Task<NfeEventResult> CancelAsync(
        MdfeCancelRequest request, MdfeServiceSettings settings, CancellationToken cancellationToken = default) =>
        RunAsync(settings, (configuration, certificate) =>
        {
            var container = ClassesFactory.CriaEvCancMDFe(request.Protocol, request.Justification);
            var result = SendEvent(MDFeTipoEvento.Cancelamento, 1, request.AccessKey, request.IssuerCnpj, request.EventAt,
                container, configuration, certificate);

            return result with { Justification = result.Justification ?? request.Justification };
        }, cancellationToken);

    public Task<NfeEventResult> IncludeDriverAsync(
        MdfeIncludeDriverRequest request, MdfeServiceSettings settings, CancellationToken cancellationToken = default) =>
        RunAsync(settings, (configuration, certificate) =>
        {
            var container = ClassesFactory.CriaEvIncCondutorMDFe(request.Name, request.Cpf);
            var result = SendEvent(MDFeTipoEvento.InclusaoDeCondutor, request.Sequence, request.AccessKey, request.IssuerCnpj,
                request.EventAt, container, configuration, certificate);

            return result with { Sequence = result.Sequence ?? request.Sequence };
        }, cancellationToken);

    public Task<IReadOnlyList<MdfeNotClosed>> ListNotClosedAsync(
        string issuerCnpj, MdfeServiceSettings settings, CancellationToken cancellationToken = default) =>
        RunAsync(settings, (configuration, certificate) =>
        {
            var request = ClassesFactory.CriarConsMDFeNaoEnc(issuerCnpj, configuration);
            Validate(() => request.ValidarSchema(configuration));

            var url = UrlHelper.ObterUrlServico(configuration.VersaoWebService.TipoAmbiente).MDFeConsNaoEnc;
            var response = new MDFeConsNaoEnc(Wsdl(url, configuration, certificate)).mdfeConsNaoEnc(request.CriaRequestWs());
            return MdfeSefazResponseMapper.FromNotClosed(response.OuterXml);
        }, cancellationToken);

    /// <summary>
    /// Monta o <c>eventoMDFe</c> como o <c>FactoryEvento</c> da Zeus (Id = "ID" + tpEvento + chave + seq),
    /// mas sem um objeto MDF-e e assinando com o certificado aberto. O órgão é a UF da chave.
    /// </summary>
    internal static MDFeEventoMDFe BuildEvent(
        MDFeTipoEvento type, int sequence, string accessKey, string issuerCnpj, DateTimeOffset eventAt,
        MDFeEventoContainer container, MDFeConfiguracao configuration)
    {
        var version = configuration.VersaoWebService.VersaoLayout;
        return new MDFeEventoMDFe
        {
            Versao = version,
            InfEvento = new MDFeInfEvento(version)
            {
                Id = $"ID{(long)type}{accessKey}{sequence:D2}",
                COrgao = (Estado)int.Parse(accessKey[..2]),
                TpAmb = configuration.VersaoWebService.TipoAmbiente,
                CNPJ = issuerCnpj,
                ChMDFe = accessKey,
                // A Zeus formata com o fuso da máquina: converte o instante para a hora local.
                DhEvento = eventAt.ToLocalTime().DateTime,
                TpEvento = type,
                NSeqEvento = checked((byte)sequence),
                DetEvento = new MDFeDetEvento { VersaoServico = version, EventoContainer = container },
            },
        };
    }

    private static NfeEventResult SendEvent(
        MDFeTipoEvento type, int sequence, string accessKey, string issuerCnpj, DateTimeOffset eventAt,
        MDFeEventoContainer container, MDFeConfiguracao configuration, X509Certificate2 certificate)
    {
        var evento = BuildEvent(type, sequence, accessKey, issuerCnpj, eventAt, container, configuration);
        evento.Signature = AssinaturaDigital.Assina(evento, evento.InfEvento.Id, certificate);
        Validate(() => evento.ValidarSchema(configuration));

        // O XML enviado é guardado verbatim no procEventoMDFe (o assinado, não uma nova serialização).
        var sentXml = evento.XmlString();
        var request = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
        request.LoadXml(sentXml);

        var url = UrlHelper.ObterUrlServico(configuration.VersaoWebService.TipoAmbiente).MDFeRecepcaoEvento;
        var response = new MDFeRecepcaoEvento(Wsdl(url, configuration, certificate)).mdfeRecepcaoEvento(request);
        return MdfeSefazResponseMapper.FromEvent(sentXml, response.OuterXml);
    }

    internal static WsdlConfiguracao Wsdl(string url, MDFeConfiguracao configuration, X509Certificate2 certificate) => new()
    {
        Url = url,
        Versao = LayoutVersion,
        CodigoIbgeEstado = configuration.VersaoWebService.UfEmitente.GetCodigoIbgeEmString(),
        CertificadoDigital = certificate,
        TimeOut = configuration.VersaoWebService.TimeOut,
    };

    /// <summary>A Zeus MDF-e lança Exception crua ("Erros da validação : ...") na falha de XSD.</summary>
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
        MdfeServiceSettings settings, Func<MDFeConfiguracao, X509Certificate2, T> call, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            // A serialização da Zeus MDF-e lê o leiaute do singleton global.
            MdfeZeusConfiguration.PinGlobalLayout();

            try
            {
                return call(MdfeZeusConfiguration.Create(settings), settings.Certificate);
            }
            catch (Exception e) when (e is WebException or HttpRequestException or TimeoutException or SocketException
                                          or (IOException and not (FileNotFoundException or DirectoryNotFoundException))
                                          or TaskCanceledException)
            {
                throw new NfeCommunicationException(e.Message, e);
            }
        }, cancellationToken);
}
