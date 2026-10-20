using NFe.Utils.Evento;
using NFe.Utils.Validacao;
using DFe.Classes.Flags;
using NFe.Classes.Servicos.Evento;
using NFe.Classes.Servicos.Tipos;
using SiagroB1.Domain.Enums;
using SiagroB1.Fiscal.Certificates;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Fiscal.Tests.Support;

namespace SiagroB1.Fiscal.Tests.Nfe;

/// <summary>
/// A Zeus valida o envEvento da manifestação no XSD antes de enviar (envConfRecebto_v1.00.xsd). Sem os XSD em
/// Schemas/ ela estoura FileNotFoundException — a mesma armadilha do cancelamento e da CC-e.
/// </summary>
public class NfeManifestationEventSchemaTests
{
    private const string AccessKey = "35261012345678000195550010000001231481516230";

    private static (string Xml, evento Event) EnvEvento(NfeManifestationRequest request)
    {
        using var certificate = CertificateLoader.Load(TestCertificates.CreatePfx(), TestCertificates.Password);
        var settings = new NfeServiceSettings(
            NfeEnvironment.Homologation, "SP", certificate, NfeServiceSettings.DefaultSchemasDirectory);
        var configuration = NfeZeusConfiguration.Create(settings);

        var evento = NfeManifestationEventBuilder.Build(request, configuration);
        evento.Assina(certificate, configuration.Certificado.SignatureMethodSignedXml,
            configuration.Certificado.DigestMethodReference, false);

        var xml = new envEvento { versao = "1.00", idLote = 1, evento = [evento] }.ObterXmlString();
        Validador.Valida(ServicoNFe.RecepcaoEventoManifestacaoDestinatario, VersaoServico.Versao100, xml, cfgServico: configuration);
        return (xml, evento);
    }

    private static NfeManifestationRequest Request(NfeManifestationType type, string? justification = null) =>
        new(AccessKey, type, "98765432000110", justification,
            new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.FromHours(-3)));

    [Fact]
    public void ManifestationEvent_NotPerformed_CarriesJustification_AndValidatesSchema()
    {
        var (xml, evento) = EnvEvento(Request(NfeManifestationType.NotPerformed, "Mercadoria recusada na entrega por avaria"));

        Assert.Contains("<tpEvento>210240</tpEvento>", xml);
        Assert.Contains("<cOrgao>91</cOrgao>", xml);
        Assert.Contains("<xJust>Mercadoria recusada na entrega por avaria</xJust>", xml);
        Assert.Equal($"ID210240{AccessKey}01", evento.infEvento.Id);
    }

    [Theory]
    [InlineData(NfeManifestationType.Confirmation, "210200", "Confirmacao da Operacao")]
    [InlineData(NfeManifestationType.Acknowledgement, "210210", "Ciencia da Operacao")]
    [InlineData(NfeManifestationType.Unknown, "210220", "Desconhecimento da Operacao")]
    public void ManifestationEvent_OtherTypes_ValidateWithoutJustification(NfeManifestationType type, string code, string description)
    {
        var (xml, _) = EnvEvento(Request(type));

        Assert.Contains($"<tpEvento>{code}</tpEvento>", xml);
        Assert.Contains($"<descEvento>{description}</descEvento>", xml);
        Assert.DoesNotContain("xJust", xml);
    }

    [Fact]
    public void Manifestation_schema_set_is_shipped_with_the_build()
    {
        foreach (var file in new[]
                 {
                     "envConfRecebto_v1.00.xsd", "leiauteConfRecebto_v1.00.xsd", "confRecebto_v1.00.xsd",
                     "retEnvConfRecebto_v1.00.xsd", "tiposBasico_v1.03.xsd", "xmldsig-core-schema_v1.01.xsd",
                 })
            Assert.True(File.Exists(Path.Combine(NfeServiceSettings.DefaultSchemasDirectory, file)), $"Schema ausente: {file}");
    }

    [Theory]
    [InlineData(135, true)]
    [InlineData(136, true)]
    [InlineData(155, true)]
    [InlineData(573, false)]
    [InlineData(494, false)]
    public void Manifestation_registered_statuses_include_136(int code, bool expected) =>
        Assert.Equal(expected, NfeStatusCodes.IsManifestationRegistered(code));

    [Theory]
    [InlineData("<distNSU><ultNSU>000000000000123</ultNSU></distNSU>")]
    [InlineData("<consChNFe><chNFe>" + AccessKey + "</chNFe></consChNFe>")]
    public void Distribution_request_validates_against_the_schema(string query)
    {
        using var certificate = CertificateLoader.Load(TestCertificates.CreatePfx(), TestCertificates.Password);
        var configuration = NfeZeusConfiguration.Create(new NfeServiceSettings(
            NfeEnvironment.Homologation, "SP", certificate, NfeServiceSettings.DefaultSchemasDirectory));
        var xml = $"<distDFeInt versao=\"1.01\" xmlns=\"http://www.portalfiscal.inf.br/nfe\"><tpAmb>2</tpAmb><cUFAutor>35</cUFAutor>" +
                  $"<CNPJ>98765432000110</CNPJ>{query}</distDFeInt>";

        Assert.Null(Record.Exception(() =>
            Validador.Valida(ServicoNFe.NFeDistribuicaoDFe, VersaoServico.Versao100, xml, cfgServico: configuration)));
    }
}
