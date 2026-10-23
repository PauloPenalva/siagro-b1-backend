using System.Text.RegularExpressions;
using SiagroB1.Domain.Enums;
using SiagroB1.Fiscal.Certificates;
using SiagroB1.Fiscal.Mdfe;
using SiagroB1.Fiscal.Tests.Support;

namespace SiagroB1.Fiscal.Tests.Mdfe;

public class MdfeXmlBuilderTests
{
    private static SignedMdfe Sign(MdfeIssueInput input)
    {
        using var certificate = CertificateLoader.Load(TestCertificates.CreatePfx(), TestCertificates.Password);
        var settings = new MdfeServiceSettings(NfeEnvironment.Homologation, "SP", certificate, MdfeZeusConfiguration.DefaultSchemasDirectory);
        return MdfeSigner.SignAndValidate(MdfeXmlBuilder.Build(input), settings);
    }

    [Fact]
    public void Build_sp_internal_trip_validates_against_xsd()
    {
        var signed = Sign(MdfeTestData.Input());

        Assert.Equal(44, signed.AccessKey.Length);
        Assert.Equal("58", signed.AccessKey.Substring(20, 2));
        Assert.Contains("<mod>58</mod>", signed.Xml);
        Assert.Contains("<tpEmit>1</tpEmit>", signed.Xml);
        Assert.DoesNotContain("<tpTransp>", signed.Xml);
        Assert.Contains("<modal>1</modal>", signed.Xml);
        Assert.Equal(2, Regex.Matches(signed.Xml, "<infMunDescarga>").Count);
        Assert.Contains($"<cMunDescarga>3550308</cMunDescarga><xMunDescarga>São Paulo</xMunDescarga><infCTe><chCTe>{MdfeTestData.CteKey1}</chCTe>", signed.Xml);
        Assert.Contains($"<cMunDescarga>3509502</cMunDescarga><xMunDescarga>Campinas</xMunDescarga><infCTe><chCTe>{MdfeTestData.CteKey2}</chCTe>", signed.Xml);
        Assert.Contains("<qCTe>2</qCTe>", signed.Xml);
        Assert.Contains("<cUnid>01</cUnid>", signed.Xml);
        Assert.Contains("<veicReboque>", signed.Xml);
        Assert.Contains("<xNome>CEAGUI CEREAIS LTDA</xNome>", signed.Xml);
        Assert.Contains("<Signature", signed.Xml);
    }

    [Fact]
    public void Build_with_ciot_and_payment_emits_infANTT()
    {
        var input = MdfeTestData.Input() with
        {
            Ciot = new MdfeCiot("123456789012", "12345678000195"),
            Contractors = [new MdfeContractor("33444555000122", "ARMAZEM DESTINO LTDA")],
            TollVoucher = new MdfeTollVoucher("11222333000181", "987654", 150m),
            Payment = new MdfePayment
            {
                Contractor = new MdfeContractor("33444555000122", "ARMAZEM DESTINO LTDA"),
                Components = [new MdfePaymentComponent("99", 5000m, "FRETE")],
                Total = 5000m,
                IndPag = 1,
                Installments = [new MdfePaymentInstallment(1, new DateOnly(2026, 11, 23), 5000m)],
                BankCode = "001",
                Agency = "1234",
            },
            Insurance = new MdfeInsurance(1, null, "SEGURADORA X", "11222333000181", "APOL123", ["AVB1"]),
        };

        var signed = Sign(input);

        Assert.Contains("<infCIOT><CIOT>123456789012</CIOT>", signed.Xml);
        Assert.Contains("<infPag>", signed.Xml);
        Assert.Contains("<indPag>1</indPag>", signed.Xml);
        Assert.Contains("<infPrazo>", signed.Xml);
        Assert.Contains("<valePed>", signed.Xml);
        Assert.Contains("<infContratante>", signed.Xml);
        Assert.Contains("<nApol>APOL123</nApol>", signed.Xml);
    }

    [Fact]
    public void Build_single_cte_emits_infLotacao_with_zips()
    {
        var signed = Sign(MdfeTestData.SingleCte());

        Assert.Contains("<infLotacao><infLocalCarrega><CEP>18440000</CEP></infLocalCarrega><infLocalDescarrega><CEP>01001000</CEP></infLocalDescarrega></infLotacao>", signed.Xml);
        Assert.DoesNotContain("<infLotacao>", Sign(MdfeTestData.Input()).Xml);
    }

    [Fact]
    public void Build_cleans_double_spaces_in_free_text()
    {
        var signed = Sign(MdfeTestData.Input() with { AdditionalInfo = "A  B " });

        Assert.Contains("<infCpl>A B</infCpl>", signed.Xml);
    }

    [Fact]
    public void Schema_error_throws_NfeValidationException()
    {
        var input = MdfeTestData.Input() with { Issuer = MdfeTestData.Input().Issuer with { Rntrc = "12" } };

        Assert.Throws<SiagroB1.Fiscal.Nfe.NfeValidationException>(() => Sign(input));
    }

    [Fact]
    public void Build_route_ufs_emits_infPercurso_in_order()
    {
        var input = MdfeTestData.Input() with
        {
            UfEnd = "MT",
            RouteUfs = ["PR", "MS"],
            UnloadingCities = [new MdfeUnloadingCity("5103403", "Cuiabá", [MdfeTestData.CteKey1, MdfeTestData.CteKey2])],
        };

        var signed = Sign(input);

        Assert.Contains("<infPercurso><UFPer>PR</UFPer></infPercurso><infPercurso><UFPer>MS</UFPer></infPercurso>", signed.Xml);
    }
}
