using SiagroB1.Application.Services.Mdfe;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Application.Tests.Mdfe;

/// <summary>O que falta para o MDF-e poder ser confirmado (erros) e o que só merece aviso.</summary>
public class MdfeReadinessValidatorTests
{
    private static TransportManifest Ready(int ctes = 2)
    {
        var m = new TransportManifest
        {
            BranchCode = "01", UfStart = "SP", UfEnd = "SP", TractionPlate = "ABC1D23", DriverCpf = "52998224725", DriverName = "JOAO",
            WeightUnit = "01", GrossWeight = 1000, CargoType = "05", ProductDescription = "SOJA",
            InsuranceResponsible = "1", InsurerTaxId = "11222333000181", InsurerName = "SEGURADORA", PolicyNumber = "APOL1",
            CiotNumber = "123456789012", CiotResponsibleTaxId = "11222333000181",
        };
        m.Payments.Add(new TransportManifestPayment { ContractorTaxId = "52998224725", ContractorName = "B", Total = 1 });
        for (var i = 0; i < ctes; i++)
            m.Ctes.Add(new TransportManifestCte { CteAccessKey = new string('5', 44), UnloadingCityIbge = "3509502", UnloadingCityName = "CAMPINAS", Endorsements = "AV" + i });
        m.CteCount = ctes;
        m.Vehicles.Add(new TransportManifestVehicle { IsTraction = true, Plate = "ABC1D23", BodyType = "03", WheelType = "03", Uf = "SP" });
        return m;
    }

    private static BranchMdfeSettings Settings(bool requiresCiot = false) => new() { BranchCode = "01", Enabled = true, RequiresCiot = requiresCiot };

    private static (IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings) Run(TransportManifest m, BranchMdfeSettings? s = null,
        string? rntrc = "11382119", string? zip = "01001000") => MdfeReadinessValidator.Validate(m, s ?? Settings(), rntrc, zip);

    [Fact]
    public void Validator_accepts_ready_manifest()
    {
        var (errors, warnings) = Run(Ready());
        Assert.Empty(errors);
        Assert.Empty(warnings);
    }

    [Fact]
    public void Validator_requires_endorsement_number_on_every_cte()
    {
        var m = Ready(3);
        var ctes = m.Ctes.ToList();
        ctes[0].TransportDocument = new TransportDocument { BranchCode = "01", Number = 11 };
        ctes[1].Endorsements = null;
        ctes[1].TransportDocument = new TransportDocument { BranchCode = "01", Number = 12 };
        ctes[2].Endorsements = " ";
        ctes[2].TransportDocument = new TransportDocument { BranchCode = "01", Number = 13 };

        Assert.Contains("Averbe os CT-e antes de incluir no MDF-e: 12, 13.", Run(m).Errors);
    }

    [Theory]
    [InlineData("responsible")]
    [InlineData("taxid")]
    [InlineData("name")]
    [InlineData("policy")]
    public void Validator_requires_insurance(string missing)
    {
        var m = Ready();
        switch (missing)
        {
            case "responsible": m.InsuranceResponsible = null; break;
            case "taxid": m.InsurerTaxId = "123"; break;
            case "name": m.InsurerName = " "; break;
            default: m.PolicyNumber = null; break;
        }

        Assert.Contains(MdfeReadinessValidator.InsuranceRequired, Run(m).Errors);
    }

    [Fact]
    public void Validator_contractor_responsible_needs_contractor()
    {
        var m = Ready();
        m.InsuranceResponsible = "2";
        m.Payments.Single().ContractorTaxId = "";
        Assert.Contains(MdfeReadinessValidator.InsuranceResponsibleRequired, Run(m).Errors);

        m.Contractors.Add(new TransportManifestContractor { TaxId = "52998224725", Name = "B" });
        Assert.Empty(Run(m).Errors);
    }

    [Fact]
    public void Validator_requires_kg_or_ton_weight()
    {
        var noWeight = Ready();
        noWeight.GrossWeight = 0;
        var m3 = Ready();
        m3.WeightUnit = "00";

        Assert.Contains(MdfeReadinessValidator.WeightRequired, Run(noWeight).Errors);
        Assert.Contains(MdfeReadinessValidator.WeightUnitInvalid, Run(m3).Errors);
    }

    [Fact]
    public void Validator_ciot_requires_payment()
    {
        var m = Ready();
        m.Payments.Clear();

        Assert.Contains("Com CIOT informado, preencha as informações de pagamento do frete.", Run(m).Errors);
    }

    [Fact]
    public void Validator_ciot_needs_12_digits_and_responsible()
    {
        var m = Ready();
        m.CiotNumber = "123";
        m.CiotResponsibleTaxId = null;

        var errors = Run(m).Errors;
        Assert.Contains(MdfeReadinessValidator.CiotLength, errors);
        Assert.Contains(MdfeReadinessValidator.CiotResponsibleRequired, errors);
    }

    [Fact]
    public void Validator_single_cte_requires_zips()
    {
        var m = Ready(ctes: 1);

        Assert.Contains("Carga lotação: informe o CEP de carregamento e de descarregamento.", Run(m).Errors);
        m.LoadingZip = "01001000";
        m.UnloadingZip = "13010000";
        m.Ncm = "12019000";
        Assert.Empty(Run(m).Errors);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("1201")]
    public void Validator_single_cte_requires_ncm(string? ncm)
    {
        var m = Ready(ctes: 1);
        m.LoadingZip = "01001000";
        m.UnloadingZip = "13010000";
        m.Ncm = ncm;

        Assert.Contains("Carga lotação: informe o NCM do produto predominante (8 dígitos).", Run(m).Errors);
        Assert.Contains(MdfeReadinessValidator.SingleCteRequiresNcm, Run(m).Warnings);
        Assert.DoesNotContain(MdfeReadinessValidator.SingleCteRequiresNcm, Run(Ready(2)).Errors);
        Assert.DoesNotContain(MdfeReadinessValidator.SingleCteRequiresNcm, Run(Ready(2)).Warnings);
    }

    [Fact]
    public void Validator_requires_driver_rntrc_issuer_zip_and_enabled_branch()
    {
        var m = Ready();
        m.DriverCpf = null;

        var errors = Run(m, new BranchMdfeSettings { BranchCode = "01", Enabled = false }, rntrc: null, zip: null).Errors;

        Assert.Contains(MdfeReadinessValidator.DriverRequired, errors);
        Assert.Contains(MdfeReadinessValidator.RntrcRequired, errors);
        Assert.Contains(MdfeReadinessValidator.IssuerZipRequired, errors);
        Assert.Contains(errors, e => e.Contains("não está habilitada a emitir MDF-e"));
    }

    [Theory]
    [InlineData(null, "11222333000181")]
    [InlineData(" ", "11222333000181")]
    [InlineData(null, null)]
    public void Validator_requires_ciot_when_rntrc_present(string? ciot, string? responsible)
    {
        var m = Ready();
        m.CiotNumber = ciot;
        m.CiotResponsibleTaxId = responsible;

        var (errors, _) = Run(m);

        Assert.Contains("CIOT obrigatório no MDF-e quando há RNTRC (rejeição 684 da SEFAZ): informe o CIOT e o responsável.", errors);
    }

    [Fact]
    public void Validator_does_not_demand_ciot_without_rntrc_beyond_rntrc_error()
    {
        var m = Ready();
        m.CiotNumber = null;

        var (errors, _) = Run(m, rntrc: null);

        Assert.Contains(MdfeReadinessValidator.RntrcRequired, errors);
        Assert.DoesNotContain(MdfeReadinessValidator.CiotRequiredWithRntrc, errors);
    }

    [Fact]
    public void Validator_refuses_invalid_main_driver_even_with_valid_additional_driver()
    {
        var m = Ready();
        m.DriverCpf = "11111111112";
        m.Drivers.Add(new TransportManifestDriver { LineNum = 1, Cpf = "52998224725", Name = "OUTRO" });

        Assert.Contains(MdfeReadinessValidator.MainDriverInvalid, Run(m).Errors);
    }
}
