using System.Security.Cryptography.X509Certificates;
using DFe.Utils;
using SiagroB1.Domain.Enums;
using SiagroB1.Fiscal.Certificates;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Fiscal.Tests.Support;

namespace SiagroB1.Fiscal.Tests.Nfe;

/// <summary>
/// Assinatura com certificado de teste e validação no XSD oficial (010d v1.03) — a verificação
/// possível sem certificado real nem credenciamento na SEFAZ.
/// </summary>
public class NfeSignerTests
{
    private static X509Certificate2 Certificate(string cnpj = "12345678000195") =>
        CertificateLoader.Load(TestCertificates.CreatePfx(cnpj), TestCertificates.Password);

    private static NfeServiceSettings Settings(X509Certificate2 certificate) =>
        new(NfeEnvironment.Homologation, "SP", certificate, NfeServiceSettings.DefaultSchemasDirectory);

    [Fact]
    public void Signed_xml_validates_against_the_official_schema()
    {
        using var certificate = Certificate();
        var signed = NfeSigner.BuildSignAndValidate(NfeTestData.Input(), Settings(certificate));

        Assert.Contains("<Signature", signed.Xml);
        Assert.Contains("NF-E EMITIDA EM AMBIENTE DE HOMOLOGACAO - SEM VALOR FISCAL", signed.Xml);
    }

    [Fact]
    public void Cest_and_full_volume_validate_against_the_official_schema()
    {
        using var certificate = Certificate();
        var input = NfeTestData.Input() with
        {
            Items = [NfeTestData.Item() with { Cest = "0600500" }],
            Volume = new NfeVolume(40, "SACO", "CEAGUI", "1 A 40"),
        };

        var signed = NfeSigner.BuildSignAndValidate(input, Settings(certificate));

        Assert.Contains("<CEST>0600500</CEST><indEscala>S</indEscala>", signed.Xml);
        Assert.Contains("<vol><qVol>40</qVol><esp>SACO</esp><marca>CEAGUI</marca><nVol>1 A 40</nVol>", signed.Xml);
    }

    [Fact]
    public void Access_key_is_valid_and_reflects_the_document()
    {
        using var certificate = Certificate();
        var signed = NfeSigner.BuildSignAndValidate(NfeTestData.Input(), Settings(certificate));

        Assert.Equal(44, signed.AccessKey.Length);
        Assert.True(ChaveFiscal.ChaveValida(signed.AccessKey));
        Assert.StartsWith("352610", signed.AccessKey);                 // cUF 35 + AAMM 2610
        Assert.Equal("12345678000195", signed.AccessKey.Substring(6, 14));
        Assert.Equal("55", signed.AccessKey.Substring(20, 2));
        Assert.Equal("001", signed.AccessKey.Substring(22, 3));
        Assert.Equal("000000123", signed.AccessKey.Substring(25, 9));
        Assert.Equal("48151623", signed.AccessKey.Substring(35, 8));
        Assert.Contains($"Id=\"NFe{signed.AccessKey}\"", signed.Xml);
    }

    [Fact]
    public void Alphanumeric_issuer_cnpj_produces_a_valid_key()
    {
        using var certificate = Certificate("12ABC34501DE35");
        var signed = NfeSigner.BuildSignAndValidate(
            NfeTestData.Input(issuerTaxId: "12ABC34501DE35"), Settings(certificate));

        Assert.Equal("12ABC34501DE35", signed.AccessKey.Substring(6, 14));
        Assert.True(ChaveFiscal.ChaveValida(signed.AccessKey));
    }

    [Fact]
    public void Schema_violation_is_reported_as_validation_error()
    {
        using var certificate = Certificate();
        var input = NfeTestData.Input() with { Items = [NfeTestData.Item() with { Ncm = "1201" }] };

        var ex = Assert.Throws<NfeValidationException>(() => NfeSigner.BuildSignAndValidate(input, Settings(certificate)));

        Assert.Contains("NCM", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
