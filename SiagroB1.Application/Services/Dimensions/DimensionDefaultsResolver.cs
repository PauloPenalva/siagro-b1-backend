using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Dimensions;

/// <summary>
/// Preenche as dimensões vazias de um documento, na ordem: cabeçalho, padrão do parceiro
/// (só na dimensão de papel Partner) e padrão da natureza. Nunca sobrescreve valor preenchido;
/// candidato inexistente ou inativo é pulado em silêncio. Só STANDALONE.
/// </summary>
public class DimensionDefaultsResolver(IUnitOfWork db, IConfiguration configuration)
{
    public async Task ApplyAsync(IDimensionFields target, IDimensionFields? header, string? partnerCode, int? usageCode)
    {
        if (!ErpMode.IsStandalone(configuration))
        {
            return;
        }

        var activeDimensions = await db.Context.AccountingDimensions.AsNoTracking()
            .Where(x => x.Active)
            .ToListAsync();

        var empty = activeDimensions
            .Where(x => string.IsNullOrWhiteSpace(target.GetDim(x.Number)))
            .ToList();
        var needLedger = string.IsNullOrWhiteSpace(target.LedgerAccountCode);

        if (empty.Count == 0 && !needLedger)
        {
            return;
        }

        string? partnerDefault = null;
        if (!string.IsNullOrWhiteSpace(partnerCode) && empty.Any(x => x.Role == DimensionRole.Partner))
        {
            partnerDefault = await db.Context.BusinessPartners.AsNoTracking()
                .Where(x => x.CardCode == partnerCode)
                .Select(x => x.DefaultDimensionCode)
                .FirstOrDefaultAsync();
        }

        Domain.Entities.Usage? usage = usageCode is null
            ? null
            : await db.Context.Usages.AsNoTracking().FirstOrDefaultAsync(x => x.Code == usageCode.Value);

        // Códigos ativos por dimensão, carregados uma vez.
        var emptyNumbers = empty.Select(x => x.Number).ToList();
        var valid = emptyNumbers.Count == 0
            ? []
            : await new DimensionCodeQueryService(db).QueryAll()
                .Where(x => !x.Inactive && emptyNumbers.Contains(x.DimensionNumber))
                .Select(x => new { x.DimensionNumber, x.Code })
                .ToListAsync();

        foreach (var dimension in empty)
        {
            var candidates = new List<string?> { header?.GetDim(dimension.Number) };
            if (dimension.Role == DimensionRole.Partner)
            {
                candidates.Add(partnerDefault);
            }

            candidates.Add(usage?.GetDim(dimension.Number));

            foreach (var raw in candidates)
            {
                var code = DimensionCodeValidator.NormalizeCode(raw);
                if (code is not null && valid.Any(x => x.DimensionNumber == dimension.Number && x.Code == code))
                {
                    target.SetDim(dimension.Number, code);
                    break;
                }
            }
        }

        if (needLedger)
        {
            var candidates = new[]
            {
                DimensionCodeValidator.NormalizeLedger(header?.LedgerAccountCode),
                DimensionCodeValidator.NormalizeLedger(usage?.LedgerAccountCode),
            };

            foreach (var code in candidates)
            {
                if (code is not null
                    && await db.Context.LedgerAccounts.AsNoTracking()
                        .AnyAsync(x => x.Code == code && !x.Inactive && x.AllowsPosting))
                {
                    target.LedgerAccountCode = code;
                    break;
                }
            }
        }
    }
}
