using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Certificates;
using SiagroB1.Fiscal.Tests.Support;

namespace SiagroB1.Fiscal.Tests.Certificates;

/// <summary>Titular, CNPJ (OID ICP-Brasil), validade e chave privada do .pfx enviado.</summary>
public class CertificateInspectorTests
{
    [Fact]
    public void Reads_the_cnpj_from_the_icp_brasil_oid()
    {
        var pfx = TestCertificates.CreatePfx(icpBrasilCnpj: "68583898000101", commonName: "OUTRA RAZAO");

        var info = CertificateInspector.Inspect(pfx, TestCertificates.Password);

        Assert.Equal("68583898000101", info.TaxId);
        Assert.True(info.HasPrivateKey);
        Assert.Contains("OUTRA RAZAO", info.Subject);
    }

    [Fact]
    public void Falls_back_to_the_cnpj_at_the_end_of_the_common_name()
    {
        var pfx = TestCertificates.CreatePfx(icpBrasilCnpj: null, commonName: "CEAGUI LTDA:12345678000195");

        Assert.Equal("12345678000195", CertificateInspector.Inspect(pfx, TestCertificates.Password).TaxId);
    }

    [Fact]
    public void Accepts_an_alphanumeric_cnpj()
    {
        var pfx = TestCertificates.CreatePfx(icpBrasilCnpj: "12ABC34501DE35", commonName: "ALFA LTDA");

        Assert.Equal("12ABC34501DE35", CertificateInspector.Inspect(pfx, TestCertificates.Password).TaxId);
    }

    [Fact]
    public void Wrong_password_is_a_business_error()
    {
        var pfx = TestCertificates.CreatePfx();

        var ex = Assert.Throws<DefaultException>(() => CertificateInspector.Inspect(pfx, "errada"));

        Assert.Contains("senha", ex.Message);
    }

    [Fact]
    public void Wrong_password_message_keeps_the_technical_detail()
    {
        var pfx = TestCertificates.CreatePfx();

        var ex = Assert.Throws<DefaultException>(() => CertificateInspector.Inspect(pfx, "errada"));

        Assert.Contains("detalhe técnico", ex.Message);
    }

    [Fact]
    public void Not_a_pfx_is_a_business_error()
    {
        Assert.Throws<DefaultException>(() => CertificateInspector.Inspect([1, 2, 3], "x"));
    }

    [Fact]
    public void Reports_a_pfx_without_private_key()
    {
        var pfx = TestCertificates.CreatePfx(withPrivateKey: false);

        Assert.False(CertificateInspector.Inspect(pfx, TestCertificates.Password).HasPrivateKey);
    }

    [Fact]
    public void Reports_the_validity()
    {
        var notAfter = new DateTimeOffset(2027, 3, 10, 12, 0, 0, TimeSpan.Zero);
        var pfx = TestCertificates.CreatePfx(notAfter: notAfter);

        var info = CertificateInspector.Inspect(pfx, TestCertificates.Password);

        Assert.Equal(notAfter.UtcDateTime.Date, info.ValidUntil.ToUniversalTime().Date);
    }
}
