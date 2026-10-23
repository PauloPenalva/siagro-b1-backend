using Microsoft.EntityFrameworkCore;
using SiagroB1.Commons.Validation;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Mdfe;

/// <summary>
/// Confere se o MDF-e tem tudo o que a SEFAZ exige. O montador do XML confia no que recebe, então as regras de
/// conteúdo (peso, CIOT, pagamento, lotação, condutor, RNTRC, CEP do emitente) moram aqui. Erros bloqueiam o
/// Confirmar; avisos só informam na gravação.
/// </summary>
public class MdfeReadinessValidator(IUnitOfWork db)
{
    public const string WeightRequired = "Informe o peso bruto da carga (maior que zero).";
    public const string WeightUnitInvalid = "O peso bruto do MDF-e deve estar em KG ou TON.";
    public const string CiotLength = "O CIOT deve ter 12 dígitos.";
    public const string CiotResponsibleRequired = "Informe o CPF ou CNPJ (válido) do responsável pelo CIOT.";
    public const string CiotRequiresPayment = "Com CIOT informado, preencha as informações de pagamento do frete.";
    public const string SingleCteRequiresZips = "Carga lotação: informe o CEP de carregamento e de descarregamento.";
    public const string DriverRequired = "Informe ao menos um condutor (CPF válido).";
    public const string TractionRequired = "Informe o veículo de tração.";
    public const string CtesRequired = "O MDF-e precisa de ao menos um CT-e.";
    public const string RntrcRequired = "Informe o RNTRC da transportadora na Configuração de Transporte da filial.";
    public const string IssuerZipRequired = "Informe o CEP do endereço da filial emitente.";
    public const string InsuranceRequired =
        "Seguro da carga é obrigatório no MDF-e: informe seguradora, CNPJ e apólice na configuração de transporte da filial (Cadastros → Filiais → Transportadora).";
    public const string InsuranceResponsibleRequired =
        "Seguro da carga: com o contratante como responsável, informe o contratante (CPF/CNPJ) do MDF-e.";
    public const string CiotRequiredWarning = "A filial exige CIOT: informe o CIOT antes de transmitir o MDF-e.";

    /// <summary>Carrega configuração, RNTRC e CEP da filial e valida.</summary>
    public async Task<(IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)> ValidateAsync(TransportManifest m)
    {
        var settings = await db.Context.BranchMdfeSettings.AsNoTracking().FirstOrDefaultAsync(x => x.BranchCode == m.BranchCode);
        var rntrc = await db.Context.BranchTransportSettings.AsNoTracking().Where(x => x.BranchCode == m.BranchCode)
            .Select(x => x.CompanyRntrc).FirstOrDefaultAsync();
        var zip = await db.Context.Branchs.AsNoTracking().Where(x => x.Code == m.BranchCode).Select(x => x.ZipCode).FirstOrDefaultAsync();
        return Validate(m, settings, rntrc, zip);
    }

    public static (IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings) Validate(
        TransportManifest m, BranchMdfeSettings? s, string? rntrc, string? issuerZipCode)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (s is not { Enabled: true })
            errors.Add($"A filial {m.BranchCode} não está habilitada a emitir MDF-e: ative na Configuração do MDF-e.");
        if (string.IsNullOrWhiteSpace(rntrc)) errors.Add(RntrcRequired);
        if (Digits(issuerZipCode).Length != 8) errors.Add(IssuerZipRequired);

        if (!MdfeInsuranceSnapshot.IsComplete(m)) errors.Add(InsuranceRequired);
        else if (m.InsuranceResponsible?.Trim() == "2"
                 && Digits(m.Contractors.FirstOrDefault()?.TaxId ?? m.Payments.FirstOrDefault()?.ContractorTaxId).Length is not (11 or 14))
            errors.Add(InsuranceResponsibleRequired);

        if (m.Ctes.Count == 0) errors.Add(CtesRequired);
        if (m.WeightUnit is not ("01" or "02")) errors.Add(WeightUnitInvalid);
        if (m.GrossWeight <= 0) errors.Add(WeightRequired);

        if (!string.IsNullOrWhiteSpace(m.CiotNumber))
        {
            if (Digits(m.CiotNumber).Length != 12) errors.Add(CiotLength);
            var responsible = Digits(m.CiotResponsibleTaxId);
            if (responsible.Length is not (11 or 14) || !TaxIdValidator.IsValid(responsible)) errors.Add(CiotResponsibleRequired);
            if (m.Payments.Count == 0) errors.Add(CiotRequiresPayment);
        }
        else if (s is { RequiresCiot: true })
        {
            warnings.Add(CiotRequiredWarning);
        }

        if (m.Ctes.Count == 1 && (Digits(m.LoadingZip).Length != 8 || Digits(m.UnloadingZip).Length != 8))
            errors.Add(SingleCteRequiresZips);

        var mainDriverOk = Digits(m.DriverCpf).Length == 11 && TaxIdValidator.IsValid(m.DriverCpf!);
        if (!mainDriverOk && !m.Drivers.Any(d => d.Cpf.Length == 11 && TaxIdValidator.IsValid(d.Cpf)))
            errors.Add(DriverRequired);

        if (string.IsNullOrWhiteSpace(m.TractionPlate) || !m.Vehicles.Any(v => v.IsTraction)) errors.Add(TractionRequired);

        return (errors, warnings);
    }

    internal static string Digits(string? value) => value is null ? string.Empty : new string(value.Where(char.IsAsciiDigit).ToArray());
}
