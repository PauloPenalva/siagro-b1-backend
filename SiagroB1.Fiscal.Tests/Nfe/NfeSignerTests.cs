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
    private static NfeServiceSettings Settings(string cnpj = "12345678000195") => new(
        NfeEnvironment.Homologation, "SP",
        CertificateLoader.Load(TestCertificates.CreatePfx(cnpj), TestCertificates.Password),
        NfeServiceSettings.DefaultSchemasDirectory);

    [Fact]
    public void Signed_xml_validates_against_the_official_schema()
    {
        var signed = NfeSigner.BuildSignAndValidate(NfeTestData.Input(), Settings());

        Assert.Contains("<Signature", signed.Xml);
        Assert.Contains("NF-E EMITIDA EM AMBIENTE DE HOMOLOGACAO - SEM VALOR FISCAL", signed.Xml);
    }

    [Fact]
    public void Access_key_is_valid_and_reflects_the_document()
    {
        var signed = NfeSigner.BuildSignAndValidate(NfeTestData.Input(), Settings());

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
        var signed = NfeSigner.BuildSignAndValidate(
            NfeTestData.Input(issuerTaxId: "12ABC34501DE35"), Settings("12ABC34501DE35"));

        Assert.Equal("12ABC34501DE35", signed.AccessKey.Substring(6, 14));
        Assert.True(ChaveFiscal.ChaveValida(signed.AccessKey));
    }

    [Fact]
    public void Schema_violation_is_reported_as_validation_error()
    {
        var input = NfeTestData.Input() with { Items = [NfeTestData.Item() with { Ncm = "1201" }] };

        var ex = Assert.Throws<NfeValidationException>(() => NfeSigner.BuildSignAndValidate(input, Settings()));

        Assert.Contains("NCM", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
