using Microsoft.EntityFrameworkCore;
using SiagroB1.Commons.Validation;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.SpedFiscal;

public interface ISpedFiscalSettingsService
{
    Task<SpedFiscalSettings?> GetAsync(string branchCode);

    Task<SpedFiscalSettings> SaveAsync(SpedFiscalSettings settings, string user);
}

/// <summary>Parâmetros do SPED Fiscal por filial: valida e grava (upsert pela filial).</summary>
public class SpedFiscalSettingsService(IUnitOfWork db) : ISpedFiscalSettingsService
{
    public Task<SpedFiscalSettings?> GetAsync(string branchCode) =>
        db.Context.SpedFiscalSettings.AsNoTracking().FirstOrDefaultAsync(s => s.BranchCode == branchCode);

    public async Task<SpedFiscalSettings> SaveAsync(SpedFiscalSettings settings, string user)
    {
        var branch = settings.BranchCode?.Trim();
        if (string.IsNullOrEmpty(branch)) throw new BusinessException("Informe a filial.");

        var profile = settings.Profile?.Trim().ToUpperInvariant();
        if (profile is not ("A" or "B" or "C")) throw new BusinessException("Perfil deve ser A, B ou C.");
        if (settings.ActivityType is not (0 or 1)) throw new BusinessException("Tipo de atividade deve ser 0 ou 1.");
        if (string.IsNullOrWhiteSpace(settings.AccountantName)) throw new BusinessException("Informe o nome do contabilista.");

        var cpf = TaxIdValidator.Normalize(settings.AccountantCpf);
        if (cpf.Length != 11 || !TaxIdValidator.IsValid(cpf)) throw new BusinessException("CPF do contabilista inválido.");

        if (string.IsNullOrWhiteSpace(settings.AccountantCrc)) throw new BusinessException("Informe o CRC do contabilista.");
        if (string.IsNullOrWhiteSpace(settings.IcmsRevenueCode)) throw new BusinessException("Informe o código de receita do ICMS.");
        if (settings.IcmsDueDay is < 1 or > 31) throw new BusinessException("Dia de vencimento deve estar entre 1 e 31.");

        var cnpj = Digits(settings.AccountantCnpj);
        if (cnpj is not null && !TaxIdValidator.IsValid(cnpj)) throw new BusinessException("CNPJ do contabilista inválido.");

        var row = await db.Context.SpedFiscalSettings.FirstOrDefaultAsync(s => s.BranchCode == branch);
        if (row is null)
        {
            row = new SpedFiscalSettings { BranchCode = branch };
            db.Context.SpedFiscalSettings.Add(row);
        }

        row.Profile = profile;
        row.ActivityType = settings.ActivityType;
        row.AccountantName = settings.AccountantName.Trim();
        row.AccountantCpf = cpf;
        row.AccountantCrc = settings.AccountantCrc.Trim();
        row.AccountantCnpj = cnpj;
        row.AccountantZipCode = Digits(settings.AccountantZipCode);
        row.AccountantStreet = Text(settings.AccountantStreet);
        row.AccountantNumber = Text(settings.AccountantNumber);
        row.AccountantComplement = Text(settings.AccountantComplement);
        row.AccountantDistrict = Text(settings.AccountantDistrict);
        row.AccountantPhone = Digits(settings.AccountantPhone);
        row.AccountantEmail = Text(settings.AccountantEmail);
        row.AccountantMunicipalityCode = Digits(settings.AccountantMunicipalityCode);
        row.IcmsRevenueCode = settings.IcmsRevenueCode.Trim();
        row.IcmsDueDay = settings.IcmsDueDay;
        row.UpdatedAt = DateTime.Now;
        row.UpdatedBy = user;
        await db.SaveChangesAsync();
        return row;
    }

    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Só os dígitos (CEP, telefone, IBGE); vazio vira nulo. CNPJ alfanumérico usa <see cref="TaxIdValidator.Normalize"/>.</summary>
    private static string? Digits(string? value)
    {
        var normalized = TaxIdValidator.Normalize(value);
        return normalized.Length == 0 ? null : normalized;
    }
}
