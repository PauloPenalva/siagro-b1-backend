using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;
using static SiagroB1.Application.Services.Accounting.AccountingAssignmentLookup;

namespace SiagroB1.Application.Services.Accounting;

/// <summary>
/// Amarração exigida pela conta contábil (CT1_CCOBRG/ACCUST/ITOBRG/ACITEM), ao confirmar uma nota ou
/// criar um título/adiantamento/lançamento. Um rateio só satisfaz "exige CC" se toda linha da versão
/// vigente na data do documento tem CC (idem item). Linha sem conta passa. Só STANDALONE.
/// </summary>
public class AccountingAssignmentGuard(IUnitOfWork db, IConfiguration configuration)
{
    public async Task EnsureAsync(IEnumerable<IAccountingAssignment> rows, Func<int, string>? rowLabel = null,
        DateTime? documentDate = null)
    {
        if (!ErpMode.IsStandalone(configuration))
        {
            return;
        }

        var date = (documentDate ?? DateTime.Today).Date;
        var index = 0;

        foreach (var row in rows)
        {
            var current = index++;
            var ledger = NormalizeLedger(row.LedgerAccountCode);
            if (ledger is null)
            {
                continue;
            }

            var account = await db.Context.LedgerAccounts.AsNoTracking()
                .Where(x => x.Code == ledger)
                .Select(x => new
                {
                    x.AcceptsCostCenter, x.RequiresCostCenter, x.AcceptsAccountingItem, x.RequiresAccountingItem,
                })
                .FirstOrDefaultAsync();
            if (account is null)
            {
                continue;
            }

            var rule = Normalize(row.DistributionRuleCode);
            var lines = rule is null ? [] : await RuleLinesAsync(db, rule, date);
            var suffix = rowLabel is null ? string.Empty : $" (linha {rowLabel(current)})";

            var hasCenter = Normalize(row.CostCenterCode) is not null;
            var ruleAllCenters = lines.Count > 0 && lines.All(x => Normalize(x.CostCenterCode) is not null);
            var ruleAnyCenter = lines.Any(x => Normalize(x.CostCenterCode) is not null);

            if (account.RequiresCostCenter && !hasCenter && !ruleAllCenters)
            {
                throw new DefaultException($"A conta {ledger} exige centro de custo{suffix}.");
            }

            if (!account.AcceptsCostCenter && (hasCenter || ruleAnyCenter))
            {
                throw new DefaultException($"A conta {ledger} não aceita centro de custo{suffix}.");
            }

            var hasItem = Normalize(row.AccountingItemCode) is not null;
            var ruleAllItems = lines.Count > 0 && lines.All(x => Normalize(x.AccountingItemCode) is not null);
            var ruleAnyItem = lines.Any(x => Normalize(x.AccountingItemCode) is not null);

            if (account.RequiresAccountingItem && !hasItem && !ruleAllItems)
            {
                throw new DefaultException($"A conta {ledger} exige item contábil{suffix}.");
            }

            if (!account.AcceptsAccountingItem && (hasItem || ruleAnyItem))
            {
                throw new DefaultException($"A conta {ledger} não aceita item contábil{suffix}.");
            }
        }
    }
}
