using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Accounting;

/// <summary>
/// Consultas comuns da amarração contábil. A comparação de código é case-insensitive, porque podem
/// existir códigos legados em minúsculas (o SQL Server já compara assim; o ToUpper cobre o InMemory).
/// </summary>
internal static class AccountingAssignmentLookup
{
    public static string? Normalize(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed.ToUpperInvariant();
    }

    public static string? NormalizeLedger(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    /// <summary>CC analítico e ativo; com <paramref name="date"/>, também vigente nessa data.</summary>
    public static Task<bool> IsUsableCostCenterAsync(IUnitOfWork db, string code, DateTime? date)
    {
        var upper = code.ToUpperInvariant();
        var day = date?.Date;
        return db.Context.CostCenters.AsNoTracking().AnyAsync(x =>
            x.Code.ToUpper() == upper && !x.Inactive && x.Class == AccountingEntityClass.Analytic
            && (day == null || ((x.ValidFrom == null || x.ValidFrom <= day) && (x.ValidTo == null || x.ValidTo >= day))));
    }

    /// <summary>Item contábil analítico e ativo; com <paramref name="date"/>, também vigente nessa data.</summary>
    public static Task<bool> IsUsableAccountingItemAsync(IUnitOfWork db, string code, DateTime? date)
    {
        var upper = code.ToUpperInvariant();
        var day = date?.Date;
        return db.Context.AccountingItems.AsNoTracking().AnyAsync(x =>
            x.Code.ToUpper() == upper && !x.Inactive && x.Class == AccountingEntityClass.Analytic
            && (day == null || ((x.ValidFrom == null || x.ValidFrom <= day) && (x.ValidTo == null || x.ValidTo >= day))));
    }

    public static Task<bool> IsActiveRuleAsync(IUnitOfWork db, string code)
    {
        var upper = code.ToUpperInvariant();
        return db.Context.DistributionRules.AsNoTracking().AnyAsync(x => x.Code.ToUpper() == upper && !x.Inactive);
    }

    public static Task<bool> IsPostingLedgerAsync(IUnitOfWork db, string code) =>
        db.Context.LedgerAccounts.AsNoTracking().AnyAsync(x => x.Code == code && !x.Inactive && x.AllowsPosting);

    /// <summary>Linhas da versão do rateio vigente em <paramref name="date"/>; vazio se não houver.</summary>
    public static async Task<IReadOnlyList<DistributionRuleLine>> RuleLinesAsync(IUnitOfWork db, string code, DateTime date)
    {
        var upper = code.ToUpperInvariant();
        var day = date.Date;
        var version = await db.Context.DistributionRuleVersions.AsNoTracking()
            .Include(x => x.Lines)
            .Where(x => x.RuleCode.ToUpper() == upper && x.ValidFrom <= day)
            .OrderByDescending(x => x.ValidFrom)
            .FirstOrDefaultAsync();
        return version?.Lines ?? [];
    }
}
