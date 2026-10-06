using DFe.Classes.Flags;
using NFe.Classes.Servicos.Evento;
using NFe.Classes.Servicos.Tipos;
using NFe.Utils.Evento;
using NFe.Utils.Validacao;
using SiagroB1.Domain.Enums;
using SiagroB1.Fiscal.Certificates;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Fiscal.Tests.Support;

namespace SiagroB1.Fiscal.Tests.Nfe;

/// <summary>
/// A Zeus valida o envEvento da CC-e no XSD antes de enviar (envCCe_v1.00.xsd). Sem os XSD em Schemas/ ela
/// estoura FileNotFoundException — a armadilha que o cancelamento só pegou no E2E.
/// </summary>
public class NfeCorrectionEventSchemaTests
{
    private const string AccessKey = "35261012345678000195550010000001231481516230";

    private static string EnvEvento(string correction)
    {
        using var certificate = CertificateLoader.Load(TestCertificates.CreatePfx(), TestCertificates.Password);
        var settings = new NfeServiceSettings(
            NfeEnvironment.Homologation, "SP", certificate, NfeServiceSettings.DefaultSchemasDirectory);
        var configuration = NfeZeusConfiguration.Create(settings);

        var evento = new evento
        {
            versao = "1.00",
            infEvento = new infEventoEnv
            {
                Id = $"ID110110{AccessKey}02",
                cOrgao = configuration.cUF,
                tpAmb = configuration.tpAmb,
                CNPJ = AccessKey.Substring(6, 14),
                chNFe = AccessKey,
                dhEvento = new DateTimeOffset(2026, 10, 6, 10, 0, 0, TimeSpan.FromHours(-3)),
                tpEvento = NFeTipoEvento.TeNfeCartaCorrecao,
                nSeqEvento = 2,
                verEvento = "1.00",
                detEvento = new detEvento
                {
                    versao = "1.00",
                    descEvento = "Carta de Correcao",
                    xCorrecao = correction,
                    xCondUso = "A Carta de Correcao e disciplinada pelo paragrafo 1o-A do art. 7o do Convenio S/N, de 15 de dezembro de 1970 e pode ser utilizada para regularizacao de erro ocorrido na emissao de documento fiscal, desde que o erro nao esteja relacionado com: I - as variaveis que determinam o valor do imposto tais como: base de calculo, aliquota, diferenca de preco, quantidade, valor da operacao ou da prestacao; II - a correcao de dados cadastrais que implique mudanca do remetente ou do destinatario; III - a data de emissao ou de saida.",
                },
            },
        };
        evento.Assina(certificate, configuration.Certificado.SignatureMethodSignedXml,
            configuration.Certificado.DigestMethodReference, false);

        var xml = new envEvento { versao = "1.00", idLote = 1, evento = [evento] }.ObterXmlString();
        Validador.Valida(ServicoNFe.RecepcaoEventoCartaCorrecao, VersaoServico.Versao100, xml, cfgServico: configuration);
        return xml;
    }

    [Fact]
    public void Correction_event_validates_against_the_official_schema()
    {
        Assert.Null(Record.Exception(() => EnvEvento(NfeCorrectionText.Normalize("Onde se le “Placa ABC1D23”\r\nleia-se Placa XYZ9K87."))));
    }

    [Fact]
    public void Correction_schema_set_is_shipped_with_the_build()
    {
        foreach (var file in new[]
                 {
                     "envCCe_v1.00.xsd", "leiauteCCe_v1.00.xsd", "CCe_v1.00.xsd", "procCCeNFe_v1.00.xsd",
                     "retEnvCCe_v1.00.xsd", "tiposBasico_v1.03.xsd", "xmldsig-core-schema_v1.01.xsd",
                 })
            Assert.True(File.Exists(Path.Combine(NfeServiceSettings.DefaultSchemasDirectory, file)), $"Schema ausente: {file}");
    }
}
