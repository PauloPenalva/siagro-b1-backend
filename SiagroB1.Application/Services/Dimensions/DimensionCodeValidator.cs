using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Dimensions;

/// <summary>
/// Normaliza (trim, maiúsculas, vazio vira null) e valida os códigos de dimensão de um documento.
/// Só valida o que mudou em relação ao estado anterior: um centro inativado depois de usado
/// não impede abrir e confirmar o documento antigo. Só STANDALONE.
/// </summary>
public class DimensionCodeValidator(IUnitOfWork db, IConfiguration configuration)
{
    public async Task NormalizeAndValidateAsync(IDimensionFields target, IDimensionFields? previous = null)
    {
        if (!ErpMode.IsStandalone(configuration))
        {
            return;
        }

        // Normaliza todos os campos do alvo (a conta contábil mantém a caixa).
        for (var n = 1; n <= 5; n++)
        {
            target.SetDim(n, NormalizeCode(target.GetDim(n)));
        }

        target.LedgerAccountCode = NormalizeLedger(target.LedgerAccountCode);

        var changedDims = new List<(int Number, string Code)>();
        for (var n = 1; n <= 5; n++)
        {
            var value = target.GetDim(n);
            if (value is null)
            {
                continue;
            }

            if (previous is not null && NormalizeCode(previous.GetDim(n)) == value)
            {
                continue;
            }

            changedDims.Add((n, value));
        }

        if (changedDims.Count > 0)
        {
            var dimensions = await db.Context.AccountingDimensions.AsNoTracking().ToListAsync();
            var numbers = changedDims.Select(x => x.Number).ToList();
            var codes = changedDims.Select(x => x.Code).ToList();
            var valid = await new DimensionCodeQueryService(db).QueryAll()
                .Where(x => !x.Inactive && numbers.Contains(x.DimensionNumber) && codes.Contains(x.Code))
                .Select(x => new { x.DimensionNumber, x.Code })
                .ToListAsync();

            foreach (var (number, code) in changedDims)
            {
                var dimension = dimensions.FirstOrDefault(x => x.Number == number);
                var ok = dimension is { Active: true }
                    && valid.Any(x => x.DimensionNumber == number && x.Code == code);
                if (!ok)
                {
                    var name = dimension?.Name ?? $"Dimensão {number}";
                    throw new DefaultException(
                        $"O código {code} não é centro de custo nem regra de distribuição ativa da dimensão {name}.");
                }
            }
        }

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
