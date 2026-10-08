using DFe.Classes.Flags;
using NFe.Classes.Servicos.Inutilizacao;
using NFe.Classes.Servicos.Tipos;
using NFe.Utils.Inutilizacao;
using NFe.Utils.Validacao;
using SiagroB1.Domain.Enums;
using SiagroB1.Fiscal.Certificates;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Fiscal.Tests.Support;

namespace SiagroB1.Fiscal.Tests.Nfe;

/// <summary>
/// O <c>ZeusNfeSefazClient.VoidNumberAsync</c> valida o inutNFe no XSD antes de enviar
/// (<c>ValidarSchemas = true</c>). Sem os XSD da inutilização em <c>Schemas/</c> a Zeus estoura
/// <see cref="FileNotFoundException"/> — o mesmo achado do cancelamento. Monta o pedido como a Zeus
/// monta e passa pela mesma validação, sem tocar na SEFAZ.
/// </summary>
public class NfeVoidNumberSchemaTests
{
    [Fact]
    public void Void_number_request_validates_against_the_official_schema()
    {
        using var certificate = CertificateLoader.Load(TestCertificates.CreatePfx(), TestCertificates.Password);
        var settings = new NfeServiceSettings(
            NfeEnvironment.Homologation, "SP", certificate, NfeServiceSettings.DefaultSchemasDirectory);
        var configuration = NfeZeusConfiguration.Create(settings);

        var request = new inutNFe
        {
            versao = "4.00",
            infInut = new infInutEnv
            {
                Id = ExtinutNFe.ObterId(configuration.cUF, 26, "12345678000195", ModeloDocumento.NFe, 1, 233, 233),
                tpAmb = configuration.tpAmb,
                xServ = "INUTILIZAR",
                cUF = configuration.cUF,
                ano = 26,
                CNPJ = "12345678000195",
                mod = ModeloDocumento.NFe,
                serie = 1,
                nNFIni = 233,
                nNFFin = 233,
                xJust = "NF-e rejeitada e documento cancelado",
            },
        };
        request.Assina(certificate, configuration.Certificado.SignatureMethodSignedXml,
            configuration.Certificado.DigestMethodReference, false);

        var error = Record.Exception(() =>
            Validador.Valida(ServicoNFe.NfeInutilizacao, VersaoServico.Versao400, request.ObterXmlString(), cfgServico: configuration));

        Assert.Null(error);
    }

    [Fact]
    public void Void_number_schema_set_is_shipped_with_the_build()
    {
        var directory = NfeServiceSettings.DefaultSchemasDirectory;

        foreach (var file in new[]
                 {
                     "inutNFe_v4.00.xsd", "leiauteInutNFe_v4.00.xsd", "retInutNFe_v4.00.xsd", "procInutNFe_v4.00.xsd",
                     "tiposBasico_v4.00.xsd", "xmldsig-core-schema_v1.01.xsd",
                 })
            Assert.True(File.Exists(Path.Combine(directory, file)), $"Schema ausente: {file}");
    }
}
