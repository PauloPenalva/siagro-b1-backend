using Microsoft.EntityFrameworkCore;
using SiagroB1.Commons.Validation;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Mdfe;

/// <summary>
/// Confere se o MDF-e tem tudo o que a SEFAZ exige. O montador do XML confia no que recebe, então as regras de
/// conteúdo (peso, CIOT obrigatório com RNTRC, pagamento, lotação, condutor, RNTRC, CEP do emitente) moram aqui. Erros bloqueiam o
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
    public const string SingleCteRequiresNcm = "Carga lotação: informe o NCM do produto predominante (8 dígitos).";
    public const string DriverRequired = "Informe ao menos um condutor (CPF válido).";
    public const string MainDriverInvalid = "O CPF do condutor principal é inválido: corrija no CT-e ou informe um CPF válido.";
    public const string TractionRequired = "Informe o veículo de tração.";
    public const string CtesRequired = "O MDF-e precisa de ao menos um CT-e.";
    public const string RntrcRequired = "Informe o RNTRC da transportadora na Configuração de Transporte da filial.";
    public const string IssuerZipRequired = "Informe o CEP do endereço da filial emitente.";
    public const string InsuranceRequired =
        "Seguro da carga é obrigatório no MDF-e: informe seguradora, CNPJ e apólice na configuração de transporte da filial (Cadastros → Filiais → Transportadora).";
    public const string InsuranceResponsibleRequired =
        "Seguro da carga: com o contratante como responsável, informe o contratante (CPF/CNPJ) do MDF-e.";
    public const string CiotRequiredWithRntrc =
        "CIOT obrigatório no MDF-e quando há RNTRC (rejeição 684 da SEFAZ): informe o CIOT e o responsável.";
    public const string CiotRequiredWarning = "A filial exige CIOT: informe o CIOT antes de transmitir o MDF-e.";

    /// <summary>"Averbe os CT-e antes de incluir no MDF-e: {n1}, {n2}." — a SEFAZ rejeita (699) MDF-e sem nAver.</summary>
    public static string NotEndorsed(IEnumerable<int?> cteNumbers) =>
        $"Averbe os CT-e antes de incluir no MDF-e: {string.Join(", ", cteNumbers.Select(n => n?.ToString() ?? "?"))}.";

    /// <summary>Carrega configuração, RNTRC e CEP da filial e valida.</summary>
    public async Task<(IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)> ValidateAsync(TransportManifest m)
    {
        var settings = await db.Context.BranchMdfeSettings.AsNoTracking().FirstOrDefaultAsync(x => x.BranchCode == m.BranchCode);
        var rntrc = await db.Context.BranchTransportSettings.AsNoTracking().Where(x => x.BranchCode == m.BranchCode)
            .Select(x => x.CompanyRntrc).FirstOrDefaultAsync();
        var zip = await db.Context.Branchs.AsNoTracking().Where(x => x.Code == m.BranchCode).Select(x => x.ZipCode).FirstOrDefaultAsync();
        // Situação atual da averbação: ela pode ter sido cancelada depois da inclusão do CT-e.
        var cteKeys = m.Ctes.Select(c => c.TransportDocumentKey).ToList();
        var live = await db.Context.TransportDocuments.AsNoTracking().Where(d => cteKeys.Contains(d.Key))
            .Select(d => new { d.Key, d.Number, d.EndorsementStatus })
            .ToDictionaryAsync(d => d.Key, d => (d.Number, Endorsed: d.EndorsementStatus == CteEndorsementStatus.Endorsed));
        return Validate(m, settings, rntrc, zip, live);
    }

    public static (IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings) Validate(
        TransportManifest m, BranchMdfeSettings? s, string? rntrc, string? issuerZipCode,
        IReadOnlyDictionary<Guid, (int? Number, bool Endorsed)>? liveCtes = null)
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
        var notEndorsed = m.Ctes.OrderBy(c => c.LineNum).Select(c =>
            {
                (int? Number, bool Endorsed) current = default;
                var hasLive = liveCtes?.TryGetValue(c.TransportDocumentKey, out current) == true;
                var ok = !string.IsNullOrWhiteSpace(c.Endorsements) && (!hasLive || current.Endorsed);
                return (Ok: ok, Number: hasLive ? current.Number : c.TransportDocument?.Number);
            })
            .Where(x => !x.Ok).Select(x => x.Number).ToList();
        if (notEndorsed.Count > 0) errors.Add(NotEndorsed(notEndorsed));
        if (m.WeightUnit is not ("01" or "02")) errors.Add(WeightUnitInvalid);
        if (m.GrossWeight <= 0) errors.Add(WeightRequired);

        if (!string.IsNullOrWhiteSpace(m.CiotNumber))
        {
            if (Digits(m.CiotNumber).Length != 12) errors.Add(CiotLength);
            var responsible = Digits(m.CiotResponsibleTaxId);
            if (responsible.Length is not (11 or 14) || !TaxIdValidator.IsValid(responsible)) errors.Add(CiotResponsibleRequired);
            if (m.Payments.Count == 0) errors.Add(CiotRequiresPayment);
        }
        else if (!string.IsNullOrWhiteSpace(rntrc))
        {
            // Rejeição 684: o MDF-e do emitente de transporte sempre leva o RNTRC, então o CIOT é obrigatório.
            errors.Add(CiotRequiredWithRntrc);
            warnings.Add(CiotRequiredWithRntrc); // a gravação só devolve avisos: o usuário vê a pendência ao salvar
        }
        else if (s is { RequiresCiot: true })
        {
            warnings.Add(CiotRequiredWarning);
        }

        if (m.Ctes.Count == 1 && (Digits(m.LoadingZip).Length != 8 || Digits(m.UnloadingZip).Length != 8))
            errors.Add(SingleCteRequiresZips);
        // Rejeição 301: infLotacao exige o NCM do produto predominante.
        if (m.Ctes.Count == 1 && Digits(m.Ncm).Length != 8) errors.Add(SingleCteRequiresNcm);

        var mainDriverOk = Digits(m.DriverCpf).Length == 11 && TaxIdValidator.IsValid(m.DriverCpf!);
        if (!mainDriverOk && !m.Drivers.Any(d => d.Cpf.Length == 11 && TaxIdValidator.IsValid(d.Cpf)))
            errors.Add(DriverRequired);
        // O condutor principal sai no XML sempre que informado: um adicional válido não mascara o CPF inválido dele.
        if (!mainDriverOk && !string.IsNullOrWhiteSpace(m.DriverCpf)) errors.Add(MainDriverInvalid);

        if (string.IsNullOrWhiteSpace(m.TractionPlate) || !m.Vehicles.Any(v => v.IsTraction)) errors.Add(TractionRequired);

        return (errors, warnings);
    }

    internal static string Digits(string? value) => value is null ? string.Empty : new string(value.Where(char.IsAsciiDigit).ToArray());
}
