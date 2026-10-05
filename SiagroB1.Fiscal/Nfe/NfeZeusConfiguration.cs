using System.Net;
using DFe.Classes.Entidades;
using DFe.Classes.Flags;
using NFe.Classes.Informacoes.Identificacao.Tipos;
using NFe.Utils;

namespace SiagroB1.Fiscal.Nfe;

/// <summary>
/// <c>ConfiguracaoServico</c> por chamada — nunca o singleton <c>Instancia</c>, que é global e
/// misturaria filiais. ⚠️ As cinco propriedades que resolvem versões e endereços (ambiente, UF,
/// modelo, tipo de emissão, versão do layout) valem 0 por padrão e não acham endpoint nenhum:
/// todas precisam ser definidas, com a versão do layout por último.
/// </summary>
internal static class NfeZeusConfiguration
{
    public static ConfiguracaoServico Create(NfeServiceSettings settings) => new()
    {
        tpAmb = NfeXmlBuilder.Environment(settings.Environment),
        cUF = Enum.Parse<Estado>(settings.IssuerState, ignoreCase: true),
        ModeloDocumento = ModeloDocumento.NFe,
        tpEmis = TipoEmissao.teNormal,
        VersaoLayout = VersaoServico.Versao400,
        TimeOut = settings.TimeoutMilliseconds,
        ProtocoloDeSeguranca = SecurityProtocolType.Tls12,
        ValidarSchemas = true,
        ValidarCertificadoDoServidor = settings.ValidateServerCertificate,
        DiretorioSchemas = settings.SchemasDirectory,
        SalvarXmlServicos = false,
    };
}
