using System.Security.Cryptography.X509Certificates;
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
/// O <c>ZeusNfeSefazClient.CancelAsync</c> valida o envEvento no XSD antes de enviar
/// (<c>ValidarSchemas = true</c>). Sem os XSD do evento de cancelamento em <c>Schemas/</c> a Zeus
/// estoura <see cref="FileNotFoundException"/> — achado no E2E. Este teste monta o mesmo envEvento
/// que a Zeus monta e passa pela mesma rotina de validação, sem tocar na SEFAZ.
/// </summary>
public class NfeCancelEventSchemaTests
{
    private const string AccessKey = "35261012345678000195550010000001231481516230";

    [Fact]
    public void Cancellation_event_validates_against_the_official_schema()
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
                Id = $"ID110111{AccessKey}01",
                cOrgao = configuration.cUF,
                tpAmb = configuration.tpAmb,
                CNPJ = AccessKey.Substring(6, 14),
                chNFe = AccessKey,
                dhEvento = new DateTimeOffset(2026, 10, 5, 10, 0, 0, TimeSpan.FromHours(-3)),
                tpEvento = NFeTipoEvento.TeNfeCancelamento,
                nSeqEvento = 1,
                verEvento = "1.00",
                detEvento = new detEvento
                {
                    versao = "1.00",
                    descEvento = "Cancelamento",
                    nProt = "135260000000001",
                    xJust = "Teste de cancelamento em homologacao",
                },
            },
        };
        evento.Assina(certificate, configuration.Certificado.SignatureMethodSignedXml,
            configuration.Certificado.DigestMethodReference, false);

        var xml = new envEvento { versao = "1.00", idLote = 1, evento = [evento] }.ObterXmlString();

        var error = Record.Exception(() =>
            Validador.Valida(ServicoNFe.RecepcaoEventoCancelmento, VersaoServico.Versao100, xml, cfgServico: configuration));

        Assert.Null(error);
    }

    [Fact]
    public void Cancellation_event_schema_set_is_shipped_with_the_build()
    {
        var directory = NfeServiceSettings.DefaultSchemasDirectory;

        foreach (var file in new[]
                 {
                     "envEventoCancNFe_v1.00.xsd", "eventoCancNFe_v1.00.xsd", "leiauteEventoCancNFe_v1.00.xsd",
                     "retEnvEventoCancNFe_v1.00.xsd", "procEventoCancNFe_v1.00.xsd", "tiposBasico_v1.03.xsd",
                 })
            Assert.True(File.Exists(Path.Combine(directory, file)), $"Schema ausente: {file}");
    }
}
