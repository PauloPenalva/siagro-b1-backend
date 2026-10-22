using System.Security.Cryptography.X509Certificates;
using DFe.Classes.Entidades;
using SiagroB1.Domain.Enums;
using SiagroB1.Fiscal.Nfe;
using CTe.Classes;
using CTe.Classes.Servicos.Tipos;
using ZeusTpEmis = CTe.Classes.Informacoes.Tipos.tpEmis;

namespace SiagroB1.Fiscal.Cte;

/// <summary>O que a Zeus CT-e precisa para assinar, validar e falar com a SEFAZ de uma filial.</summary>
public sealed record CteServiceSettings(
    NfeEnvironment Environment,
    string IssuerState,
    X509Certificate2 Certificate,
    string SchemasDirectory,
    int TimeoutMilliseconds = 60_000)
{
    /// <summary>A pasta <c>Schemas/CTe</c> que o <c>SiagroB1.Fiscal</c> copia para a saída.</summary>
    public static string DefaultSchemasDirectory => Path.Combine(AppContext.BaseDirectory, "Schemas", "CTe");
}

/// <summary>
/// <c>ConfiguracaoServico</c> da Zeus CT-e por chamada — nunca o singleton <c>Instancia</c>, que é
/// global e misturaria filiais. O modelo 57 não é propriedade da configuração no CT-e (vai em
/// <c>ide/mod</c>, pelo builder); a versão do layout é 4.00.
/// </summary>
public static class CteZeusConfiguration
{
    /// <summary>
    /// ⚠️ A serialização das classes da Zeus CT-e (ex.: <c>ide.ProxydhEmi</c>) lê a versão do layout
    /// do singleton global <c>ConfiguracaoServico.Instancia</c>, não da configuração passada: no
    /// padrão (2.00) o <c>dhEmi</c> sai sem fuso e o XSD 4.00 recusa. O sistema só emite 4.00,
    /// então fixar o global é seguro; nada mais do singleton é usado.
    /// </summary>
    public static void PinGlobalLayout() => ConfiguracaoServico.Instancia.VersaoLayout = versao.ve400;

    public static ConfiguracaoServico Create(CteServiceSettings settings) => new()
    {
        tpAmb = NfeXmlBuilder.Environment(settings.Environment),
        cUF = Enum.Parse<Estado>(settings.IssuerState, ignoreCase: true),
        TipoEmissao = ZeusTpEmis.teNormal,
        VersaoLayout = versao.ve400,
        TimeOut = settings.TimeoutMilliseconds,
        IsValidaSchemas = true,
        DiretorioSchemas = settings.SchemasDirectory,
        IsSalvarXml = false,
    };
}
