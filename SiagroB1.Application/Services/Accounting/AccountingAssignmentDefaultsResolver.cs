using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;
using static SiagroB1.Application.Services.Accounting.AccountingAssignmentLookup;

namespace SiagroB1.Application.Services.Accounting;

/// <summary>
/// Preenche a amarração contábil vazia de um documento, por fonte: cabeçalho, produto (só nas linhas, como B1_CONTA/B1_CC/B1_ITEMCC do Protheus) e parceiro
/// (a natureza não traz padrão contábil).
/// Nunca sobrescreve valor preenchido, nunca põe CC/Item onde já há rateio (nem o contrário) e pula
/// em silêncio o candidato inválido na data do documento. Só STANDALONE.
/// </summary>
public class AccountingAssignmentDefaultsResolver(IUnitOfWork db, IConfiguration configuration)
{
    public async Task ApplyAsync(IAccountingAssignment target, IAccountingAssignment? header, string? partnerCode,
        DateTime? documentDate = null, string? itemCode = null)
    {
        if (!ErpMode.IsStandalone(configuration))
        {
            return;
        }

        var date = (documentDate ?? DateTime.Today).Date;

        var partner = string.IsNullOrWhiteSpace(partnerCode)
            ? null
            : await db.Context.BusinessPartners.AsNoTracking()
                .Where(x => x.CardCode == partnerCode)
                .Select(x => new { x.DefaultAccountingItemCode, x.DefaultLedgerAccountCode })
                .FirstOrDefaultAsync();

        var product = string.IsNullOrWhiteSpace(itemCode)
            ? null
            : await db.Context.Items.AsNoTracking()
                .Where(x => x.ItemCode == itemCode)
                .Select(x => new { x.CostCenterCode, x.AccountingItemCode, x.LedgerAccountCode })
                .FirstOrDefaultAsync();

        // Precedência por FONTE (cabeçalho → produto → parceiro; o produto oferece CC/Item, o parceiro só Item): a primeira fonte que traz rateio ou CC/Item
        // decide o modo. Com rateio (e nada de CC/Item digitado na linha), a linha recebe o rateio; senão CC/Item
        // vêm dessa fonte e das seguintes, nunca um rateio. Linha com rateio digitado não recebe CC/Item.
        if (string.IsNullOrWhiteSpace(target.DistributionRuleCode))
        {
            var costCenterMode = !string.IsNullOrWhiteSpace(target.CostCenterCode)
                                 || !string.IsNullOrWhiteSpace(target.AccountingItemCode);
            var sources = new (string? CostCenter, string? Item, string? Rule)[]
            {
                (header?.CostCenterCode, header?.AccountingItemCode, header?.DistributionRuleCode),
                (product?.CostCenterCode, product?.AccountingItemCode, null),
                (null, partner?.DefaultAccountingItemCode, null),
            };

            foreach (var source in sources)
            {
                var costCenter = await FirstAsync(c => IsUsableCostCenterAsync(db, c, date), source.CostCenter);
                var item = await FirstAsync(c => IsUsableAccountingItemAsync(db, c, date), source.Item);

                if (!costCenterMode)
                {
                    var rule = await FirstAsync(c => IsActiveRuleAsync(db, c), source.Rule);
                    if (rule is not null)
                    {
                        target.DistributionRuleCode = rule;
                        break;
                    }

                    if (costCenter is null && item is null)
                        continue;

                    costCenterMode = true;
                }

                if (string.IsNullOrWhiteSpace(target.CostCenterCode) && costCenter is not null)
                    target.CostCenterCode = costCenter;
                if (string.IsNullOrWhiteSpace(target.AccountingItemCode) && item is not null)
                    target.AccountingItemCode = item;
            }
        }

        if (string.IsNullOrWhiteSpace(target.LedgerAccountCode))
        {
            target.LedgerAccountCode = await FirstLedgerAsync(header?.LedgerAccountCode, product?.LedgerAccountCode,
                    partner?.DefaultLedgerAccountCode)
                ?? target.LedgerAccountCode;
        }
    }

    private static async Task<string?> FirstAsync(Func<string, Task<bool>> isValid, params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            var code = Normalize(candidate);
            if (code is not null && await isValid(code))
            {
                return code;
            }
        }

        return null;
    }

    private async Task<string?> FirstLedgerAsync(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            var code = NormalizeLedger(candidate);
            if (code is not null && await IsPostingLedgerAsync(db, code))
            {
                return code;
            }
        }

        return null;
    }
}
