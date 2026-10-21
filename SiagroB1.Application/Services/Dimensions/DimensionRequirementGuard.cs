using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Dimensions;

/// <summary>
/// Aplica a obrigatoriedade dos parâmetros da empresa (centro de custo e conta contábil) ao
/// confirmar uma nota ou criar um título/adiantamento/lançamento. Roda depois dos padrões e
/// antes de numerar/gravar. Não faz nada em SAPB1.
/// </summary>
public class DimensionRequirementGuard(IUnitOfWork db, IConfiguration configuration, CompanySettingsService settingsService)
{
    public DimensionRequirementGuard(IUnitOfWork db, IConfiguration configuration)
        : this(db, configuration, new CompanySettingsService(db))
    {
    }

    public async Task EnsureAsync(IEnumerable<IDimensionFields> rows, Func<int, string>? rowLabel = null)
    {
        if (!ErpMode.IsStandalone(configuration))
        {
            return;
        }

        var settings = await settingsService.GetAsync();
        if (!settings.RequireCostCenter && !settings.RequireLedgerAccount)
        {
            return;
        }

        string? dimensionName = null;
        var index = 0;
        foreach (var row in rows)
        {
            var suffix = rowLabel is null ? string.Empty : $" na linha {rowLabel(index)}";
            index++;

            if (settings.RequireCostCenter && string.IsNullOrWhiteSpace(row.Dim1Code))
            {
                dimensionName ??= (await db.Context.AccountingDimensions.AsNoTracking()
                    .Where(x => x.Number == 1).Select(x => x.Name).FirstOrDefaultAsync()) ?? "o centro de custo";
                throw new DefaultException($"Informe {dimensionName}{suffix}.");
            }

            if (settings.RequireLedgerAccount && string.IsNullOrWhiteSpace(row.LedgerAccountCode))
            {
                throw new DefaultException($"Informe a conta contábil{suffix}.");
            }
        }
    }
}
