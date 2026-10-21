using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Dimensions;

/// <summary>
/// Transforma o código digitado numa dimensão (centro de custo ou regra) em pares centro/percentual.
/// Para regra, usa a versão de maior ValidFrom que não passe da data do documento.
/// </summary>
public class DistributionRuleExpander(IUnitOfWork db)
{
    public async Task<IReadOnlyList<(string CostCenterCode, decimal Percentage)>> ExpandAsync(
        int dimensionNumber, string code, DateTime documentDate)
    {
        var normalized = (code ?? string.Empty).Trim().ToUpperInvariant();

        if (await db.Context.CostCenters.AnyAsync(x => x.Code == normalized && x.DimensionNumber == dimensionNumber))
        {
            return [(normalized, 100m)];
        }

        var rule = await db.Context.DistributionRules
            .AsNoTracking()
            .Include(x => x.Versions)
            .ThenInclude(v => v.Lines)
            .FirstOrDefaultAsync(x => x.Code == normalized && x.DimensionNumber == dimensionNumber)
            ?? throw new DefaultException($"O código {normalized} não existe na dimensão {dimensionNumber}.");

        var date = documentDate.Date;
        var version = rule.Versions
            .Where(x => x.ValidFrom.Date <= date)
            .OrderByDescending(x => x.ValidFrom)
            .FirstOrDefault()
            ?? throw new DefaultException(
                $"A regra {normalized} não tem versão vigente em {date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}.");

        return version.Lines
            .Select(x => (x.CostCenterCode, x.Percentage))
            .ToList();
    }
}
