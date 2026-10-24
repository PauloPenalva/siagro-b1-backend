using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Ecf;

public interface IEcfSettingsService
{
    Task<EcfSettings?> GetAsync(string branchCode);

    Task<EcfSettings> SaveAsync(EcfSettings settings, string user);
}

/// <summary>Parâmetros da ECF por filial: valida e grava (upsert pela filial).</summary>
public class EcfSettingsService(IUnitOfWork db) : IEcfSettingsService
{
    public Task<EcfSettings?> GetAsync(string branchCode) =>
        db.Context.EcfSettings.AsNoTracking().FirstOrDefaultAsync(s => s.BranchCode == branchCode);

    public async Task<EcfSettings> SaveAsync(EcfSettings settings, string user)
    {
        var branch = settings.BranchCode?.Trim();
        if (string.IsNullOrEmpty(branch)) throw new BusinessException("Informe a filial.");
        await EcfRules.EnsureBranchAsync(db.Context, branch);
        if (!Enum.IsDefined(settings.AssessmentForm)) throw new BusinessException("Forma de apuração inválida.");

        var cnae = EcfRules.Digits(settings.MainCnae);
        if (cnae.Length != 7) throw new BusinessException("CNAE deve ter 7 dígitos.");

        var qualification = settings.LegalEntityQualification?.Trim() ?? string.Empty;
        if (!EcfRules.IsTwoDigits(qualification)) throw new BusinessException("Qualificação da pessoa jurídica deve ter 2 dígitos.");

        var nature = settings.NatureCode?.Trim() ?? string.Empty;
        if (nature.Length != 4 || !nature.All(char.IsAsciiDigit)) throw new BusinessException("Natureza jurídica deve ter 4 dígitos.");

        EcfRules.MaxLength(settings.PreviousEcfHash?.Trim(), 40, "Hash da ECF anterior");

        var row = await db.Context.EcfSettings.FirstOrDefaultAsync(s => s.BranchCode == branch);
        if (row is null)
        {
            row = new EcfSettings { BranchCode = branch };
            db.Context.EcfSettings.Add(row);
        }

        row.AssessmentForm = settings.AssessmentForm;
        row.MainCnae = cnae;
        row.LegalEntityQualification = qualification;
        row.NatureCode = nature;
        row.PreviousEcfHash = string.IsNullOrWhiteSpace(settings.PreviousEcfHash) ? null : settings.PreviousEcfHash.Trim();
        row.UpdatedAt = DateTime.Now;
        row.UpdatedBy = user;
        await db.SaveChangesAsync();
        return row;
    }
}

public static class EcfRules
{
    internal static string Digits(string? value) =>
        new((value ?? string.Empty).Where(char.IsAsciiDigit).ToArray());

    public static bool IsTwoDigits(string value) => value.Length == 2 && value.All(char.IsAsciiDigit);

    /// <summary>Filial precisa existir: os cadastros da ECF não ficam órfãos de filial.</summary>
    internal static async Task EnsureBranchAsync(Microsoft.EntityFrameworkCore.DbContext context, string branchCode)
    {
        if (!await context.Set<SiagroB1.Domain.Entities.Branch>().AsNoTracking().AnyAsync(b => b.Code == branchCode))
            throw new BusinessException($"Filial {branchCode} não encontrada.");
    }

    internal static void MaxLength(string? value, int max, string label)
    {
        if (value is not null && value.Length > max)
            throw new BusinessException($"{label} deve ter no máximo {max} caracteres.");
    }
}
