using Microsoft.Extensions.Configuration;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;
using static SiagroB1.Application.Services.Accounting.AccountingAssignmentLookup;

namespace SiagroB1.Application.Services.Accounting;

/// <summary>
/// Normaliza (trim, maiúsculas, vazio vira null) e valida a amarração contábil de um documento.
/// Só valida o que mudou em relação a <c>previous</c>: um CC que foi bloqueado ou virou sintético
/// depois de gravado continua aceito no documento antigo. Só STANDALONE.
/// </summary>
public class AccountingAssignmentValidator(IUnitOfWork db, IConfiguration configuration)
{
    public async Task NormalizeAndValidateAsync(IAccountingAssignment target, IAccountingAssignment? previous = null,
        DateTime? documentDate = null)
    {
        if (!ErpMode.IsStandalone(configuration))
        {
            return;
        }

        var date = (documentDate ?? DateTime.Today).Date;

        target.CostCenterCode = Normalize(target.CostCenterCode);
        target.AccountingItemCode = Normalize(target.AccountingItemCode);
        target.DistributionRuleCode = Normalize(target.DistributionRuleCode);
        target.LedgerAccountCode = NormalizeLedger(target.LedgerAccountCode);

        if (target.DistributionRuleCode is not null && (target.CostCenterCode is not null || target.AccountingItemCode is not null))
        {
            throw new DefaultException("Informe o rateio ou o centro de custo/item contábil, não os dois.");
        }

        var center = target.CostCenterCode;
        if (center is not null && center != Normalize(previous?.CostCenterCode)
            && !await IsUsableCostCenterAsync(db, center, date))
        {
            throw new DefaultException(
                $"O centro de custo {center} não existe, é sintético, está bloqueado ou fora da vigência.");
        }

        var item = target.AccountingItemCode;
        if (item is not null && item != Normalize(previous?.AccountingItemCode)
            && !await IsUsableAccountingItemAsync(db, item, date))
        {
            throw new DefaultException(
                $"O item contábil {item} não existe, é sintético, está bloqueado ou fora da vigência.");
        }

        var rule = target.DistributionRuleCode;
        if (rule is not null && rule != Normalize(previous?.DistributionRuleCode) && !await IsActiveRuleAsync(db, rule))
        {
            throw new DefaultException($"O rateio {rule} não existe ou está inativo.");
        }

        var ledger = target.LedgerAccountCode;
        if (ledger is not null && ledger != NormalizeLedger(previous?.LedgerAccountCode) && !await IsPostingLedgerAsync(db, ledger))
        {
            throw new DefaultException($"A conta contábil {ledger} não existe, está inativa ou não aceita lançamento.");
        }
    }
}
