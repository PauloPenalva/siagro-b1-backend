using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Accounting;

/// <summary>
/// Transforma um rateio pré-configurado em linhas CC/Item/percentual, usando a versão de maior
/// ValidFrom que não passe da data do documento.
/// </summary>
public class DistributionRuleExpander(IUnitOfWork db)
{
    public async Task<IReadOnlyList<(string? CostCenterCode, string? AccountingItemCode, decimal Percentage)>> ExpandAsync(
        string ruleCode, DateTime documentDate)
    {
        var normalized = (ruleCode ?? string.Empty).Trim().ToUpperInvariant();

        var rule = await db.Context.DistributionRules
            .AsNoTracking()
            .Include(x => x.Versions)
            .ThenInclude(v => v.Lines)
            .FirstOrDefaultAsync(x => x.Code == normalized)
            ?? throw new DefaultException($"O rateio {normalized} não existe.");

        var date = documentDate.Date;
        var version = rule.Versions
            .Where(x => x.ValidFrom.Date <= date)
            .OrderByDescending(x => x.ValidFrom)
            .FirstOrDefault()
            ?? throw new DefaultException(
                $"O rateio {normalized} não tem versão vigente em {date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}.");

        return version.Lines
            .Select(x => (x.CostCenterCode, x.AccountingItemCode, x.Percentage))
            .ToList();
    }
}
