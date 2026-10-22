using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Accounting;

/// <summary>
/// Regras de distribuição (tabelas locais DISTRIBUTION_RULES/_VERSIONS/_LINES). Cada versão vale a partir
/// de ValidFrom; versão vigente ou passada é imutável, só versões futuras podem ser trocadas ou excluídas.
/// O "hoje" vem por parâmetro para o comportamento ser determinístico.
/// </summary>
public class DistributionRuleService(IUnitOfWork db) : IDistributionRuleService
{
    public IQueryable<DistributionRule> QueryAll()
    {
        return db.Context.DistributionRules
            .Include(x => x.Versions)
            .ThenInclude(v => v.Lines)
            .AsNoTracking();
    }

    public async Task<DistributionRule> CreateAsync(DistributionRule rule)
    {
        var code = NormalizeCode(rule.Code);
        var name = (rule.Name ?? string.Empty).Trim();

        if (code.Length == 0)
        {
            throw new DefaultException("Informe o código da regra.");
        }

        if (name.Length == 0)
        {
            throw new DefaultException("Informe o nome da regra.");
        }

        if (await db.Context.DistributionRules.AnyAsync(x => x.Code == code))
        {
            throw new DefaultException($"Regra de distribuição {code} já cadastrada.");
        }

        if (rule.Versions.Count == 0)
        {
            throw new DefaultException("Informe ao menos uma versão da regra.");
        }

        var created = new DistributionRule
        {
            Code = code,
            Name = name,
            Inactive = rule.Inactive,
        };

        foreach (var input in rule.Versions)
        {
            var version = await BuildVersionAsync(created, input);

            if (created.Versions.Any(x => x.ValidFrom == version.ValidFrom))
            {
                throw new DefaultException(
                    $"Há mais de uma versão com início em {FormatDate(version.ValidFrom)}.");
            }

            created.Versions.Add(version);
        }

        await db.Context.DistributionRules.AddAsync(created);
        await db.SaveChangesAsync();

        return created;
    }

    public async Task<DistributionRule?> UpdateHeaderAsync(string code, string name, bool inactive)
    {
        var rule = await db.Context.DistributionRules.FirstOrDefaultAsync(x => x.Code == NormalizeCode(code));

        if (rule == null)
        {
            return null;
        }

        var trimmed = (name ?? string.Empty).Trim();

        if (trimmed.Length == 0)
        {
            throw new DefaultException("Informe o nome da regra.");
        }

        rule.Name = trimmed;
        rule.Inactive = inactive;

        await db.SaveChangesAsync();

        return rule;
    }

    public async Task<DistributionRuleVersion> SaveVersionAsync(string code, DistributionRuleVersion version, DateTime today)
    {
        var rule = await db.Context.DistributionRules
            .Include(x => x.Versions)
            .ThenInclude(v => v.Lines)
            .FirstOrDefaultAsync(x => x.Code == NormalizeCode(code))
            ?? throw new DefaultException($"A regra {NormalizeCode(code)} não existe.");

        var validFrom = version.ValidFrom.Date;
        var existing = rule.Versions.FirstOrDefault(x => x.ValidFrom.Date == validFrom);

        if (existing != null && validFrom <= today.Date)
        {
            throw new DefaultException(
                $"A versão vigente em {FormatDate(validFrom)} já está em uso e não pode ser alterada; crie uma versão com nova data.");
        }

        // Mudar percentuais nunca reescreve o passado: nova versão só a partir de amanhã.
        if (existing == null && rule.Versions.Count > 0 && validFrom <= today.Date)
        {
            throw new DefaultException("A nova versão deve começar depois de hoje; versões passadas não podem ser criadas.");
        }

        var built = await BuildVersionAsync(rule, version);

        if (existing != null)
        {
            db.Context.DistributionRuleLines.RemoveRange(existing.Lines);
            built.Lines.ForEach(x => x.VersionId = existing.Id);
            await db.Context.DistributionRuleLines.AddRangeAsync(built.Lines);
            await db.SaveChangesAsync();

            return existing;
        }

        // Add explícito: chaves Guid já preenchidas fariam o EF tratar o grafo como existente (UPDATE).
        await db.Context.DistributionRuleVersions.AddAsync(built);
        await db.SaveChangesAsync();

        return built;
    }

    public async Task DeleteVersionAsync(Guid versionId, DateTime today)
    {
        var version = await db.Context.DistributionRuleVersions
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.Id == versionId)
            ?? throw new DefaultException("Versão não encontrada.");

        if (version.ValidFrom.Date <= today.Date)
        {
            throw new DefaultException(
                $"A versão vigente em {FormatDate(version.ValidFrom)} já está em uso e não pode ser excluída.");
        }

        var versionCount = await db.Context.DistributionRuleVersions.CountAsync(x => x.RuleCode == version.RuleCode);
        if (versionCount <= 1)
        {
            throw new DefaultException("A regra precisa ter ao menos uma versão.");
        }

        db.Context.DistributionRuleLines.RemoveRange(version.Lines);
        db.Context.DistributionRuleVersions.Remove(version);
        await db.SaveChangesAsync();
    }

    private async Task<DistributionRuleVersion> BuildVersionAsync(DistributionRule rule, DistributionRuleVersion input)
    {
        if (input.Lines.Count == 0)
        {
            throw new DefaultException("Informe ao menos uma linha na versão da regra.");
        }

        var versionId = Guid.NewGuid();
        var lines = new List<DistributionRuleLine>();

        foreach (var line in input.Lines)
        {
            var centerCode = NormalizeOptional(line.CostCenterCode);
            var itemCode = NormalizeOptional(line.AccountingItemCode);
            var label = $"{centerCode ?? "-"}/{itemCode ?? "-"}";

            if (centerCode is null && itemCode is null)
            {
                throw new DefaultException("Informe o centro de custo ou o item contábil em cada linha do rateio.");
            }

            if (lines.Any(x => x.CostCenterCode == centerCode && x.AccountingItemCode == itemCode))
            {
                throw new DefaultException("O par centro de custo/item contábil se repete na versão.");
            }

            // Só analítico e ativo; a vigência é conferida no documento, na data dele.
            if (centerCode is not null && !await AccountingAssignmentLookup.IsUsableCostCenterAsync(db, centerCode, null))
            {
                throw new DefaultException($"O centro de custo {centerCode} não existe, é sintético ou está bloqueado.");
            }

            if (itemCode is not null && !await AccountingAssignmentLookup.IsUsableAccountingItemAsync(db, itemCode, null))
            {
                throw new DefaultException($"O item contábil {itemCode} não existe, é sintético ou está bloqueado.");
            }

            if (line.Percentage <= 0)
            {
                throw new DefaultException($"O percentual da linha {label} deve ser maior que zero.");
            }

            lines.Add(new DistributionRuleLine
            {
                Id = Guid.NewGuid(),
                VersionId = versionId,
                CostCenterCode = centerCode,
                AccountingItemCode = itemCode,
                Percentage = line.Percentage,
            });
        }

        if (lines.Sum(x => x.Percentage) != 100m)
        {
            throw new DefaultException("A soma dos percentuais da regra deve ser 100%.");
        }

        return new DistributionRuleVersion
        {
            Id = versionId,
            RuleCode = rule.Code,
            ValidFrom = input.ValidFrom.Date,
            Lines = lines,
        };
    }

    private static string NormalizeCode(string? code)
    {
        return (code ?? string.Empty).Trim().ToUpperInvariant();
    }

    private static string FormatDate(DateTime date)
    {
        return date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
    }

    private static string? NormalizeOptional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed.ToUpperInvariant();
    }
}
