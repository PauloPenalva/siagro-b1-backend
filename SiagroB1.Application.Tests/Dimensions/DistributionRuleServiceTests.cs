using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Dimensions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Dimensions;

public class DistributionRuleServiceTests
{
    private static readonly DateTime Today = new(2026, 10, 21);

    private static async Task<(UnitOfWork Db, DistributionRuleService Service)> CreateAsync()
    {
        var db = TestDb.CreateUnitOfWork();
        await DimensionTestData.SeedDimensions(db);
        db.Context.CostCenters.AddRange(
            new CostCenter { Code = "CC01", Name = "A", DimensionNumber = 1 },
            new CostCenter { Code = "CC02", Name = "B", DimensionNumber = 1 },
            new CostCenter { Code = "CC09", Name = "Outra dimensão", DimensionNumber = 2 });
        await db.SaveChangesAsync();
        return (db, new DistributionRuleService(db));
    }

    private static DistributionRuleVersion Version(DateTime validFrom, params (string Center, decimal Pct)[] lines) =>
        new()
        {
            ValidFrom = validFrom,
            Lines = lines.Select(x => new DistributionRuleLine { CostCenterCode = x.Center, Percentage = x.Pct }).ToList(),
        };

    private static DistributionRule Rule(string code, params DistributionRuleVersion[] versions) =>
        new() { Code = code, Name = "Rateio", DimensionNumber = 1, Versions = versions.ToList() };

    [Fact]
    public async Task Create_normalizes_code_and_stores_first_version()
    {
        var (db, service) = await CreateAsync();

        var rule = await service.CreateAsync(Rule(" r01 ", Version(Today, ("CC01", 60), ("CC02", 40))));

        Assert.Equal("R01", rule.Code);
        var stored = await db.Context.DistributionRules.Include(x => x.Versions).ThenInclude(v => v.Lines).SingleAsync();
        Assert.Single(stored.Versions);
        Assert.Equal("R01", stored.Versions[0].RuleCode);
        Assert.Equal(2, stored.Versions[0].Lines.Count);
    }

    [Fact]
    public async Task Lines_must_sum_100()
    {
        var (_, service) = await CreateAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            service.CreateAsync(Rule("R01", Version(Today, ("CC01", 60), ("CC02", 30)))));

        Assert.Equal("A soma dos percentuais da regra deve ser 100%.", ex.Message);
    }

    [Fact]
    public async Task Lines_must_be_active_centers_of_rule_dimension()
    {
        var (_, service) = await CreateAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            service.CreateAsync(Rule("R01", Version(Today, ("CC01", 50), ("CC09", 50)))));

        Assert.Equal("O centro CC09 não pertence à dimensão Dimensão 1.", ex.Message);
    }

    [Fact]
    public async Task Inactive_center_is_rejected()
    {
        var (db, service) = await CreateAsync();
        db.Context.CostCenters.Single(x => x.Code == "CC02").Inactive = true;
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            service.CreateAsync(Rule("R01", Version(Today, ("CC01", 50), ("CC02", 50)))));

        Assert.Equal("O centro CC02 está inativo.", ex.Message);
    }

    [Fact]
    public async Task Duplicate_center_in_version_rejected()
    {
        var (_, service) = await CreateAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            service.CreateAsync(Rule("R01", Version(Today, ("CC01", 50), ("CC01", 50)))));

        Assert.Equal("O centro CC01 aparece mais de uma vez na regra.", ex.Message);
    }

    [Fact]
    public async Task Code_cannot_collide_with_cost_center()
    {
        var (_, service) = await CreateAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            service.CreateAsync(Rule("cc01", Version(Today, ("CC01", 100)))));

        Assert.Equal("O código CC01 já é um centro de custo.", ex.Message);
    }

    [Fact]
    public async Task Current_or_past_version_is_immutable()
    {
        var (_, service) = await CreateAsync();
        await service.CreateAsync(Rule("R01", Version(Today, ("CC01", 60), ("CC02", 40))));

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            service.SaveVersionAsync("R01", Version(Today, ("CC01", 50), ("CC02", 50)), Today));

        Assert.Equal(
            "A versão vigente em 21/10/2026 já está em uso e não pode ser alterada; crie uma versão com nova data.",
            ex.Message);
    }

    [Fact]
    public async Task Future_version_can_be_replaced_and_deleted()
    {
        var (db, service) = await CreateAsync();
        await service.CreateAsync(Rule("R01", Version(Today, ("CC01", 60), ("CC02", 40))));
        var tomorrow = Today.AddDays(1);

        await service.SaveVersionAsync("R01", Version(tomorrow, ("CC01", 70), ("CC02", 30)), Today);
        var replaced = await service.SaveVersionAsync("R01", Version(tomorrow, ("CC01", 50), ("CC02", 50)), Today);

        var versions = await db.Context.DistributionRuleVersions.Include(v => v.Lines).ToListAsync();
        Assert.Equal(2, versions.Count);
        Assert.All(versions.Single(v => v.ValidFrom == tomorrow).Lines, l => Assert.Equal(50, l.Percentage));
        Assert.Equal(2, await db.Context.DistributionRuleLines.CountAsync(l => l.VersionId == replaced.Id));

        await service.DeleteVersionAsync(replaced.Id, Today);

        Assert.Single(await db.Context.DistributionRuleVersions.ToListAsync());
        Assert.Equal(2, await db.Context.DistributionRuleLines.CountAsync());
    }

    [Fact]
    public async Task Past_dated_new_version_is_rejected()
    {
        var (_, service) = await CreateAsync();
        await service.CreateAsync(Rule("R01", Version(Today.AddDays(-10), ("CC01", 60), ("CC02", 40))));

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            service.SaveVersionAsync("R01", Version(Today.AddDays(-5), ("CC01", 50), ("CC02", 50)), Today));

        Assert.Equal("A nova versão deve começar depois de hoje; versões passadas não podem ser criadas.", ex.Message);
    }

    [Fact]
    public async Task Today_dated_new_version_is_rejected()
    {
        var (_, service) = await CreateAsync();
        await service.CreateAsync(Rule("R01", Version(Today.AddDays(-10), ("CC01", 60), ("CC02", 40))));

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            service.SaveVersionAsync("R01", Version(Today, ("CC01", 50), ("CC02", 50)), Today));

        Assert.Equal("A nova versão deve começar depois de hoje; versões passadas não podem ser criadas.", ex.Message);
    }

    [Fact]
    public async Task Current_version_cannot_be_deleted()
    {
        var (_, service) = await CreateAsync();
        var rule = await service.CreateAsync(Rule("R01", Version(Today, ("CC01", 60), ("CC02", 40))));

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            service.DeleteVersionAsync(rule.Versions[0].Id, Today));

        Assert.Equal(
            "A versão vigente em 21/10/2026 já está em uso e não pode ser excluída.",
            ex.Message);
    }

    [Fact]
    public async Task Deleting_the_last_remaining_version_is_refused()
    {
        var (_, service) = await CreateAsync();
        var rule = await service.CreateAsync(Rule("R01", Version(Today.AddDays(1), ("CC01", 60), ("CC02", 40))));

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            service.DeleteVersionAsync(rule.Versions[0].Id, Today));

        Assert.Equal("A regra precisa ter ao menos uma versão.", ex.Message);
    }

    [Fact]
    public async Task UpdateHeader_changes_name_and_inactive()
    {
        var (_, service) = await CreateAsync();
        await service.CreateAsync(Rule("R01", Version(Today, ("CC01", 100))));

        var updated = await service.UpdateHeaderAsync("R01", "Novo nome", true);

        Assert.Equal("Novo nome", updated!.Name);
        Assert.True(updated.Inactive);
        Assert.Null(await service.UpdateHeaderAsync("ZZ", "x", false));
    }
}
