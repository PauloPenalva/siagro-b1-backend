using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Dimensions;

/// <summary>
/// Preenche a amarração contábil vazia de um documento, na ordem: cabeçalho, parceiro e natureza.
/// Nunca sobrescreve valor preenchido. Só STANDALONE.
/// </summary>
// TODO Task 3: substituir por AccountingAssignmentDefaultsResolver (pular candidato inválido,
// respeitar a exclusividade rateio x CC/Item).
public class DimensionDefaultsResolver(IUnitOfWork db, IConfiguration configuration)
{
    public async Task ApplyAsync(IAccountingAssignment target, IAccountingAssignment? header, string? partnerCode, int? usageCode)
    {
        if (!ErpMode.IsStandalone(configuration))
        {
            return;
        }

        var partner = string.IsNullOrWhiteSpace(partnerCode)
            ? null
            : await db.Context.BusinessPartners.AsNoTracking()
                .Where(x => x.CardCode == partnerCode)
                .Select(x => new { x.DefaultCostCenterCode, x.DefaultAccountingItemCode })
                .FirstOrDefaultAsync();

        Domain.Entities.Usage? usage = usageCode is null
            ? null
            : await db.Context.Usages.AsNoTracking().FirstOrDefaultAsync(x => x.Code == usageCode.Value);

        target.CostCenterCode ??= First(header?.CostCenterCode, partner?.DefaultCostCenterCode, usage?.CostCenterCode);
        target.AccountingItemCode ??= First(header?.AccountingItemCode, partner?.DefaultAccountingItemCode, usage?.AccountingItemCode);
        target.LedgerAccountCode ??= First(header?.LedgerAccountCode, usage?.LedgerAccountCode);
    }

    private static string? First(params string?[] candidates) =>
        candidates.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
}
