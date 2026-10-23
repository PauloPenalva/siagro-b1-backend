using Microsoft.EntityFrameworkCore;
using SiagroB1.Commons.Validation;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Ecd;

public interface IEcdSettingsService
{
    Task<EcdSettings?> GetAsync(string branchCode);

    Task<EcdSettings> SaveAsync(EcdSettings settings, string user);
}

/// <summary>Parâmetros da ECD por filial: valida e grava (upsert pela filial).</summary>
public class EcdSettingsService(IUnitOfWork db) : IEcdSettingsService
{
    public Task<EcdSettings?> GetAsync(string branchCode) =>
        db.Context.EcdSettings.AsNoTracking().FirstOrDefaultAsync(s => s.BranchCode == branchCode);

    public async Task<EcdSettings> SaveAsync(EcdSettings settings, string user)
    {
        var branch = settings.BranchCode?.Trim();
        if (string.IsNullOrEmpty(branch)) throw new BusinessException("Informe a filial.");
        if (settings.BookNumber < 1) throw new BusinessException("Número do livro deve ser maior que zero.");

        string? nire = null;
        if (settings.HasNire)
        {
            nire = Digits(settings.Nire);
            if (nire is null || nire.Length != 11 || !nire.All(char.IsAsciiDigit))
                throw new BusinessException("NIRE deve ter 11 dígitos.");
        }

        var plan = settings.ReferentialPlanCode?.Trim();
        if (!int.TryParse(plan, out var planNumber) || planNumber is < 1 or > 10)
            throw new BusinessException("Plano referencial deve estar entre 1 e 10.");

        if (string.IsNullOrWhiteSpace(settings.SignerName)) throw new BusinessException("Informe o nome do responsável.");
        var cpf = TaxIdValidator.Normalize(settings.SignerCpf);
        if (cpf.Length != 11 || !TaxIdValidator.IsValid(cpf)) throw new BusinessException("CPF do responsável inválido.");

        var qualification = settings.SignerQualificationCode?.Trim() ?? string.Empty;
        if (qualification.Length != 3 || !qualification.All(char.IsAsciiDigit))
            throw new BusinessException("Qualificação do assinante deve ter 3 dígitos.");

        if (string.IsNullOrWhiteSpace(settings.SignerEmail)) throw new BusinessException("Informe o e-mail do responsável.");

        var row = await db.Context.EcdSettings.FirstOrDefaultAsync(s => s.BranchCode == branch);
        if (row is null)
        {
            row = new EcdSettings { BranchCode = branch };
            db.Context.EcdSettings.Add(row);
        }

        row.BookNumber = settings.BookNumber;
        row.HasNire = settings.HasNire;
        row.Nire = nire;
        row.ConstitutiveActsDate = settings.ConstitutiveActsDate?.Date;
        row.LargeCompany = settings.LargeCompany;
        row.ReferentialPlanCode = planNumber.ToString();
        row.SignerName = settings.SignerName.Trim();
        row.SignerCpf = cpf;
        row.SignerQualificationCode = qualification;
        row.SignerEmail = settings.SignerEmail.Trim();
        row.SignerPhone = Digits(settings.SignerPhone);
        row.UpdatedAt = DateTime.Now;
        row.UpdatedBy = user;
        await db.SaveChangesAsync();
        return row;
    }

    private static string? Digits(string? value)
    {
        var normalized = TaxIdValidator.Normalize(value);
        return normalized.Length == 0 ? null : normalized;
    }
}
