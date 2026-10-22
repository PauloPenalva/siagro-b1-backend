using Microsoft.Extensions.Configuration;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Accounting;

/// <summary>Cadastros de CC, item contábil e rateios usados pelos testes de amarração contábil.</summary>
public static class AccountingTestData
{
    public static readonly DateTime Today = new(2026, 10, 21);

    public static IConfiguration Config(string erp = "STANDALONE") =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Erp"] = erp }).Build();

    public static async Task SeedAsync(IUnitOfWork db)
    {
        db.Context.CostCenters.AddRange(
            new CostCenter { Code = "CC01", Name = "Centro 1" },
            new CostCenter { Code = "CC02", Name = "Centro 2" },
            new CostCenter { Code = "SYN", Name = "Sintético", Class = AccountingEntityClass.Synthetic },
            new CostCenter { Code = "OLD", Name = "Bloqueado", Inactive = true },
            new CostCenter { Code = "EXP", Name = "Vencido", ValidTo = Today.AddDays(-1) },
            new CostCenter { Code = "FUT", Name = "Futuro", ValidFrom = Today.AddDays(1) });
        db.Context.AccountingItems.AddRange(
            new AccountingItem { Code = "IT01", Name = "Item 1" },
            new AccountingItem { Code = "IT02", Name = "Item 2" },
            new AccountingItem { Code = "ISYN", Name = "Sintético", Class = AccountingEntityClass.Synthetic },
            new AccountingItem { Code = "IOLD", Name = "Bloqueado", Inactive = true });
        await db.SaveChangesAsync();
    }

    public static DistributionRule Rule(string code, DateTime validFrom,
        params (string? Center, string? Item, decimal Pct)[] lines) =>
        new()
        {
            Code = code,
            Name = "Rateio",
            Versions = [Version(validFrom, lines)],
        };

    public static DistributionRuleVersion Version(DateTime validFrom,
        params (string? Center, string? Item, decimal Pct)[] lines) =>
        new()
        {
            ValidFrom = validFrom,
            Lines = lines.Select(x => new DistributionRuleLine
            {
                CostCenterCode = x.Center, AccountingItemCode = x.Item, Percentage = x.Pct,
            }).ToList(),
        };

    /// <summary>Grava uma regra já pronta, sem passar pelo serviço (RuleCode preenchido nas versões).</summary>
    public static async Task AddRuleAsync(IUnitOfWork db, DistributionRule rule)
    {
        foreach (var version in rule.Versions)
        {
            version.Id = Guid.NewGuid();
            version.RuleCode = rule.Code;
            foreach (var line in version.Lines)
            {
                line.Id = Guid.NewGuid();
                line.VersionId = version.Id;
            }
        }

        db.Context.DistributionRules.Add(rule);
        await db.SaveChangesAsync();
    }
}
