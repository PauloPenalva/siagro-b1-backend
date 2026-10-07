using System.Globalization;
using System.Xml.Linq;
using System.Security.Cryptography.X509Certificates;
using DFe.Utils;
using SiagroB1.Domain.Enums;
using SiagroB1.Fiscal.Certificates;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Fiscal.Payments;
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
    public void Cash_payment_without_billing_validates_against_the_official_schema()
    {
        using var certificate = Certificate();
        var input = NfeTestData.Input() with
        {
            Payment = PaymentInstallmentCalculator.Calculate("0", PaymentStartRule.IssueDate, "99", 60000m, DateOnly.FromDateTime(NfeTestData.IssuedAt.Date)),
        };

        var signed = NfeSigner.BuildSignAndValidate(input, Settings(certificate));

        Assert.Contains("<Signature", signed.Xml);
        Assert.DoesNotContain("<cobr>", signed.Xml);
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
    public void Customer_order_validates_against_the_official_schema()
    {
        using var certificate = Certificate();
        var input = NfeTestData.Input() with
        {
            Items = [NfeTestData.Item() with { OrderNumber = "PO-77", OrderItem = "1" }],
        };

        var signed = NfeSigner.BuildSignAndValidate(input, Settings(certificate));

        Assert.Contains("<xPed>PO-77</xPed><nItemPed>1</nItemPed>", signed.Xml);
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

    [Fact]
    public void Return_note_validates_against_the_official_schema()
    {
        using var certificate = Certificate();

        var signed = NfeSigner.BuildSignAndValidate(NfeTestData.ReturnInput(), Settings(certificate));

        Assert.Contains("<tpNF>0</tpNF>", signed.Xml);
        Assert.Contains("<finNFe>4</finNFe>", signed.Xml);
        Assert.DoesNotContain("<NFref>", signed.Xml);
        Assert.Contains($"<DFeReferenciado><chaveAcesso>{NfeTestData.SaleAccessKey}</chaveAcesso><nItem>1</nItem></DFeReferenciado>", signed.Xml);
        Assert.Contains("<tPag>90</tPag>", signed.Xml);
        Assert.DoesNotContain("<cobr>", signed.Xml);
    }

    [Fact]
    public void Purchase_entry_validates_against_the_official_schema()
    {
        using var certificate = Certificate();

        var signed = NfeSigner.BuildSignAndValidate(NfeTestData.PurchaseEntryInput(), Settings(certificate));

        Assert.Contains("<tpNF>0</tpNF>", signed.Xml);
        Assert.Contains("<finNFe>1</finNFe>", signed.Xml);
        Assert.Contains($"<NFref><refNFe>{NfeTestData.ProducerAccessKey}</refNFe></NFref>", signed.Xml);
        Assert.Contains("<CPF>52998224725</CPF>", signed.Xml);
    }

    [Fact]
    public void Purchase_return_validates_against_the_official_schema()
    {
        using var certificate = Certificate();

        var signed = NfeSigner.BuildSignAndValidate(NfeTestData.PurchaseReturnInput(), Settings(certificate));

        Assert.Contains("<tpNF>1</tpNF>", signed.Xml);
        Assert.Contains("<finNFe>4</finNFe>", signed.Xml);
        Assert.DoesNotContain("<NFref>", signed.Xml);
        Assert.Contains($"<DFeReferenciado><chaveAcesso>{NfeTestData.EntryAccessKey}</chaveAcesso><nItem>2</nItem></DFeReferenciado>", signed.Xml);
        Assert.Contains("<tPag>90</tPag>", signed.Xml);
    }

    private static readonly XNamespace Ns = "http://www.portalfiscal.inf.br/nfe";

    private static decimal Number(XElement? element) => decimal.Parse(element!.Value, CultureInfo.InvariantCulture);

    [Fact]
    public void Line_charges_validate_against_the_official_schema()
    {
        using var certificate = Certificate();
        var input = NfeTestData.Input() with
        {
            Items = [NfeTestData.Item() with { FreightValue = 1000m, InsuranceValue = 100m, DiscountValue = 500m, OtherExpensesValue = 400m }],
            Payment = PaymentInstallmentCalculator.Calculate("30,60", PaymentStartRule.IssueDate, "15", 61000m,
                DateOnly.FromDateTime(NfeTestData.IssuedAt.Date)),
        };

        var xml = XDocument.Parse(NfeSigner.BuildSignAndValidate(input, Settings(certificate)).Xml);

        var prod = xml.Descendants(Ns + "det").Single().Element(Ns + "prod")!;
        Assert.Equal((1000m, 100m, 500m, 400m),
            (Number(prod.Element(Ns + "vFrete")), Number(prod.Element(Ns + "vSeg")), Number(prod.Element(Ns + "vDesc")),
                Number(prod.Element(Ns + "vOutro"))));
        Assert.Equal(61000m, Number(xml.Descendants(Ns + "ICMSTot").Single().Element(Ns + "vNF")));
        Assert.Equal(61000m, Number(xml.Descendants(Ns + "vPag").Single()));
    }

    [Fact]
    public void Line_without_charges_signs_without_the_optional_prod_values()
    {
        // Review Focus 3: o det/prod não ganha vFrete/vSeg/vDesc/vOutro zerados.
        using var certificate = Certificate();

        var xml = XDocument.Parse(NfeSigner.BuildSignAndValidate(NfeTestData.Input(), Settings(certificate)).Xml);

        var prod = xml.Descendants(Ns + "det").Single().Element(Ns + "prod")!;
        Assert.Empty(prod.Elements().Where(e => e.Name.LocalName is "vFrete" or "vSeg" or "vDesc" or "vOutro"));
        Assert.Equal(60000m, Number(xml.Descendants(Ns + "ICMSTot").Single().Element(Ns + "vNF")));
    }
}
