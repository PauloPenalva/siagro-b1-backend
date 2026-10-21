using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Dimensions;

/// <summary>
/// Normaliza (trim, maiúsculas, vazio vira null) a amarração contábil de um documento e valida a
/// conta contábil. Só valida o que mudou em relação ao estado anterior. Só STANDALONE.
/// </summary>
// TODO Task 3: substituir por AccountingAssignmentValidator (CC/Item analítico, ativo e vigente,
// exclusividade rateio x CC/Item, flags da conta).
public class DimensionCodeValidator(IUnitOfWork db, IConfiguration configuration)
{
    public async Task NormalizeAndValidateAsync(IAccountingAssignment target, IAccountingAssignment? previous = null)
    {
        if (!ErpMode.IsStandalone(configuration))
        {
            return;
        }

        target.CostCenterCode = NormalizeCode(target.CostCenterCode);
        target.AccountingItemCode = NormalizeCode(target.AccountingItemCode);
        target.DistributionRuleCode = NormalizeCode(target.DistributionRuleCode);
        target.LedgerAccountCode = NormalizeLedger(target.LedgerAccountCode);

        var ledger = target.LedgerAccountCode;
        if (ledger is not null && ledger != NormalizeLedger(previous?.LedgerAccountCode))
        {
            var ok = await db.Context.LedgerAccounts.AsNoTracking()
                .AnyAsync(x => x.Code == ledger && !x.Inactive && x.AllowsPosting);
            if (!ok)
            {
                throw new DefaultException($"A conta contábil {ledger} não existe, está inativa ou não aceita lançamento.");
            }
        }
    }

    internal static string? NormalizeCode(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed.ToUpperInvariant();
    }

    internal static string? NormalizeLedger(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
