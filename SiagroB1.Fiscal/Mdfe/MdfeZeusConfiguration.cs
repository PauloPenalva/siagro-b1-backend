using System.Security.Cryptography.X509Certificates;
using DFe.Classes.Entidades;
using MDFe.Utils.Configuracoes;
using MDFe.Utils.Flags;
using SiagroB1.Domain.Enums;
using SiagroB1.Fiscal.Nfe;

namespace SiagroB1.Fiscal.Mdfe;

/// <summary>O que a Zeus MDF-e precisa para assinar, validar e falar com a SEFAZ de uma filial.</summary>
public sealed record MdfeServiceSettings(
    NfeEnvironment Environment,
    string IssuerState,
    X509Certificate2 Certificate,
    string SchemasDirectory,
    int TimeoutMilliseconds = 60_000);

/// <summary>
/// <c>MDFeConfiguracao</c> da Zeus MDF-e por chamada — nunca o singleton <c>Instancia</c>, que é
/// global e misturaria filiais. Layout 3.00.
/// <para>
/// ⚠️ Singleton global: a Zeus MDF-e tem o seu próprio (<c>MDFeConfiguracao.Instancia</c>), separado
/// do <c>CTe.Classes.ConfiguracaoServico.Instancia</c> do CT-e — fixar um não mexe no outro (o spike
/// <c>Cte_and_mdfe_global_configs_coexist</c> prova). Mas as classes do MDF-e leem a versão do global
/// sempre que não a receberam no construtor: <c>MDFeIde.ProxyDhEmi</c>/<c>ProxyDhIniViagem</c> e
/// <c>MDFeInfEvento</c> (e qualquer objeto desserializado, que usa o construtor privado). O padrão do
/// global é 0 — nem 1.00 nem 3.00 — e a serialização do <c>dhEmi</c> lança "Versão Inválida".
/// Por isso toda operação MDF-e chama <see cref="PinGlobalLayout"/> no início, e o builder ainda
/// passa <c>VersaoServico.Versao300</c> aos construtores que aceitam.
/// </para>
/// <para>
/// ⚠️ Certificado: <c>MDFeConfiguracao.X509Certificate2</c> não tem setter — é relido de
/// <c>ConfiguracaoCertificado</c> (arquivo/repositório/bytes, com cache global do DFe). Por isso, como
/// no CT-e, não use os serviços prontos (<c>ServicoMDFeRecepcao</c> etc.) nem <c>ExtMDFe.Assina</c>:
/// assine com <c>DFe.Utils.Assinatura.AssinaturaDigital.Assina</c> e o certificado já aberto, e envie
/// pelos WSDLs (<c>new MDFeRecepcaoSinc(new WsdlConfiguracao { CertificadoDigital = ... })</c>).
/// </para>
/// </summary>
public static class MdfeZeusConfiguration
{
    /// <summary>A pasta <c>Schemas/MDFe</c> que o <c>SiagroB1.Fiscal</c> copia para a saída.</summary>
    public static string DefaultSchemasDirectory => Path.Combine(AppContext.BaseDirectory, "Schemas", "MDFe");

    /// <summary>O sistema só emite MDF-e 3.00; nada mais do singleton é usado.</summary>
    public static void PinGlobalLayout() =>
        MDFeConfiguracao.Instancia.VersaoWebService.VersaoLayout = VersaoServico.Versao300;

    /// <summary>⚠️ O setter de <c>CaminhoSchemas</c> lança <c>Exception</c> se a pasta não existir.</summary>
    public static MDFeConfiguracao Create(MdfeServiceSettings settings) => new()
    {
        VersaoWebService = new MDFeVersaoWebService
        {
            TipoAmbiente = NfeXmlBuilder.Environment(settings.Environment),
            UfEmitente = Enum.Parse<Estado>(settings.IssuerState, ignoreCase: true),
            VersaoLayout = VersaoServico.Versao300,
            TimeOut = settings.TimeoutMilliseconds,
        },
        CaminhoSchemas = settings.SchemasDirectory,
        IsSalvarXml = false,
        IsAdicionaQrCode = true,
    };
}
