using System.Security.Cryptography.X509Certificates;
using SiagroB1.Domain.Enums;
using SiagroB1.Fiscal.Certificates;
using SiagroB1.Fiscal.Cte;
using SiagroB1.Fiscal.Tests.Support;

namespace SiagroB1.Fiscal.Tests.Cte;

/// <summary>Monta, assina com o certificado de teste e valida no XSD oficial do CT-e 4.00.</summary>
public class CteXmlBuilderTests
{
    private static X509Certificate2 Certificate() =>
        CertificateLoader.Load(TestCertificates.CreatePfx(), TestCertificates.Password);

    private static SignedCte Sign(CteIssueInput input)
    {
        using var certificate = Certificate();
        var settings = new CteServiceSettings(NfeEnvironment.Homologation, "SP", certificate, CteServiceSettings.DefaultSchemasDirectory);
        return CteSigner.SignAndValidate(CteXmlBuilder.Build(input), settings);
    }

    [Fact]
    public void Build_internal_sp_freight_validates_against_xsd()
    {
        var input = CteTestData.Input() with
        {
            CargoQuantities = [new CteCargoQuantity("00", "VOLUME", 40m)],
        };

        var signed = Sign(input);

        Assert.Contains("<Signature", signed.Xml);
        Assert.Contains("<mod>57</mod>", signed.Xml);
        Assert.Contains("<toma3><toma>3</toma></toma3>", signed.Xml);
        Assert.Contains("<CST>00</CST>", signed.Xml);
        Assert.Contains("<pICMS>12.00</pICMS>", signed.Xml);
        Assert.Contains($"<chave>{NfeTestData.SaleAccessKey}</chave>", signed.Xml);
        Assert.Contains("<CFOP>5352</CFOP>", signed.Xml);
        Assert.Equal(44, signed.AccessKey.Length);
        Assert.Contains($"Id=\"CTe{signed.AccessKey}\"", signed.Xml);
    }

    [Fact]
    public void Build_cleans_free_text_with_edge_and_double_spaces_and_newlines()
    {
        var dirty = CteTestData.Input();
        var input = dirty with
        {
            Sender = dirty.Sender with
            {
                Name = "  COSTA   PRODUTOR RURAL LTDA ",
                Address = dirty.Sender.Address with { Street = "AVENIDA DOS ESTADOS ", District = " CENTRO  VELHO", Complement = "   " },
            },
            Recipient = dirty.Recipient with { Name = "ARMAZEM\tDESTINO LTDA  ", Email = "  " },
            Remarks = "FRETE  CONFORME\r\nCONTRATO ",
            Observations = [new CteObservation(" CONTRATO ", " 123\n456 ")],
            PredominantProduct = " SOJA  EM GRAOS ",
        };

        var signed = Sign(input);

        Assert.Contains("<xNome>COSTA PRODUTOR RURAL LTDA</xNome>", signed.Xml);
        Assert.Contains("<xLgr>AVENIDA DOS ESTADOS</xLgr>", signed.Xml);
        Assert.Contains("<xBairro>CENTRO VELHO</xBairro>", signed.Xml);
        Assert.Contains("<xNome>ARMAZEM DESTINO LTDA</xNome>", signed.Xml);
        Assert.Contains("<xObs>FRETE CONFORME CONTRATO</xObs>", signed.Xml);
        Assert.Contains("<xTexto>123 456</xTexto>", signed.Xml);
        Assert.Contains("<proPred>SOJA EM GRAOS</proPred>", signed.Xml);
        Assert.DoesNotContain("<xCpl></xCpl>", signed.Xml);
        Assert.DoesNotContain("<xCpl/>", signed.Xml);
    }

    [Fact]
    public void Build_taker_other_uses_toma4()
    {
        var signed = Sign(CteTestData.Input() with { Taker = CteTaker.Other, OtherTaker = CteTestData.OtherTaker() });

        Assert.Contains("<toma4><toma>4</toma><CNPJ>44555666000177</CNPJ>", signed.Xml);
        Assert.Contains("<enderToma><xLgr>RUA AUGUSTA</xLgr><nro>500</nro>", signed.Xml);
        Assert.DoesNotContain("<toma3>", signed.Xml);
    }

    [Fact]
    public void Build_receiver_present_emits_receb_and_end_municipality()
    {
        var input = CteTestData.Input() with
        {
            Receiver = CteTestData.Receiver(),
            Route = new CteRoute("3521705", "Itaberá", "SP", "3509502", "Campinas", "SP"),
        };

        var signed = Sign(input);

        Assert.Contains("<receb><CNPJ>55666777000133</CNPJ>", signed.Xml);
        Assert.Contains("<cMunFim>3509502</cMunFim>", signed.Xml);
    }

    [Fact]
    public void Build_indIEToma_follows_input()
    {
        var signed = Sign(CteTestData.Input() with { TakerIndicator = 9 });

        Assert.Contains("<indIEToma>9</indIEToma>", signed.Xml);
    }

    [Fact]
    public void Schema_failure_raises_NfeValidationException()
    {
        var input = CteTestData.Input() with { Issuer = CteTestData.Input().Issuer with { Rntrc = "ABC" } };

        var error = Assert.Throws<SiagroB1.Fiscal.Nfe.NfeValidationException>(() => Sign(input));

        Assert.Contains("RNTRC", error.Message);
    }

    [Fact]
    public void Build_adds_qr_code_with_key_and_environment()
    {
        var signed = Sign(CteTestData.Input());
        Assert.Contains("<infCTeSupl><qrCodCTe>", signed.Xml);
        Assert.Contains($"chCTe={signed.AccessKey}&amp;tpAmb=2</qrCodCTe>", signed.Xml);
        // URL da tabela da Zeus: em SP a homologação usa homologacao.nfe.fazenda.sp.gov.br.
        Assert.Contains("<qrCodCTe>https://homologacao.nfe.fazenda.sp.gov.br/CTeConsulta/qrCode?chCTe=", signed.Xml);
    }

    [Fact]
    public void Build_with_ibscbs_emits_vTotDFe_after_group_with_service_total()
    {
        var input = CteTestData.Input() with
        {
            IbsCbsCst = "000", IbsCbsClassCode = "000001", IbsCbsBase = 1000m,
            IbsStateRate = 0.1m, IbsStateValue = 1m, IbsMunicipalRate = 0m, IbsMunicipalValue = 0m, CbsRate = 0.9m, CbsValue = 9m,
        };

        var signed = Sign(input);

        Assert.Contains("<vTotDFe>1000.00</vTotDFe></imp>", signed.Xml);
        Assert.True(signed.Xml.IndexOf("</IBSCBS>", StringComparison.Ordinal) < signed.Xml.IndexOf("<vTotDFe>", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_with_ibscbs_without_base_still_emits_vTotDFe()
    {
        var signed = Sign(CteTestData.Input() with { IbsCbsCst = "000", IbsCbsClassCode = "000001" });

        Assert.Contains("<vTotDFe>1000.00</vTotDFe>", signed.Xml);
    }

    [Fact]
    public void Build_without_ibscbs_omits_vTotDFe()
    {
        Assert.DoesNotContain("vTotDFe", Sign(CteTestData.Input()).Xml);
    }
}
