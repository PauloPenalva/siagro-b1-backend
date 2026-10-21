using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Accounting;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using static SiagroB1.Application.Tests.Accounting.AccountingTestData;

namespace SiagroB1.Application.Tests.Accounting;

public class DistributionRuleServiceTests
{
    private static async Task<(UnitOfWork Db, DistributionRuleService Service)> CreateAsync()
    {
        var db = TestDb.CreateUnitOfWork();
        await SeedAsync(db);
        return (db, new DistributionRuleService(db));
    }

    private static async Task<string> Throws(Func<Task> act) =>
        (await Assert.ThrowsAsync<DefaultException>(act)).Message;

    [Fact]
    public async Task Create_normalizes_codes_and_stores_cc_and_item_lines()
    {
        var (db, service) = await CreateAsync();

        var rule = await service.CreateAsync(Rule(" r01 ", Today, (" cc01 ", "it01", 60), (null, "IT02", 40)));

        Assert.Equal("R01", rule.Code);
        var stored = await db.Context.DistributionRules.Include(x => x.Versions).ThenInclude(v => v.Lines).SingleAsync();
        var lines = stored.Versions.Single().Lines.OrderByDescending(x => x.Percentage).ToList();
        Assert.Equal(("CC01", "IT01"), (lines[0].CostCenterCode, lines[0].AccountingItemCode));
        Assert.Equal((null, "IT02"), (lines[1].CostCenterCode, lines[1].AccountingItemCode));
    }

    [Fact]
    public async Task Line_needs_cost_center_or_item()
    {
        var (_, service) = await CreateAsync();

        Assert.Equal("Informe o centro de custo ou o item contábil em cada linha do rateio.",
            await Throws(() => service.CreateAsync(Rule("R01", Today, ("CC01", null, 50), (" ", null, 50)))));
    }

    [Fact]
    public async Task Lines_must_sum_100()
    {
        var (_, service) = await CreateAsync();

        Assert.Equal("A soma dos percentuais da regra deve ser 100%.",
            await Throws(() => service.CreateAsync(Rule("R01", Today, ("CC01", null, 60), ("CC02", null, 30)))));
    }

    [Fact]
    public async Task Duplicate_pair_rejected_case_insensitive()
    {
        var (_, service) = await CreateAsync();

        Assert.Equal("O par centro de custo/item contábil se repete na versão.",
            await Throws(() => service.CreateAsync(Rule("R01", Today, ("CC01", "IT01", 50), ("cc01", "it01", 50)))));
    }

    [Fact]
    public async Task Same_center_with_different_items_is_allowed()
    {
        var (_, service) = await CreateAsync();

        await service.CreateAsync(Rule("R01", Today, ("CC01", "IT01", 50), ("CC01", "IT02", 50)));
    }

    [Theory]
    [InlineData("SYN", null, "O centro de custo SYN não existe, é sintético ou está bloqueado.")]
    [InlineData("OLD", null, "O centro de custo OLD não existe, é sintético ou está bloqueado.")]
    [InlineData("ZZ", null, "O centro de custo ZZ não existe, é sintético ou está bloqueado.")]
    [InlineData(null, "ISYN", "O item contábil ISYN não existe, é sintético ou está bloqueado.")]
    [InlineData(null, "IOLD", "O item contábil IOLD não existe, é sintético ou está bloqueado.")]
    public async Task Lines_need_analytic_active_entities(string? center, string? item, string message)
    {
        var (_, service) = await CreateAsync();

        Assert.Equal(message, await Throws(() => service.CreateAsync(Rule("R01", Today, (center, item, 100)))));
    }

    [Fact]
    public async Task Legacy_lowercase_center_matches()
    {
        var (db, service) = await CreateAsync();
        db.Context.CostCenters.Add(new CostCenter { Code = "low1", Name = "Legado" });
        await db.SaveChangesAsync();

        await service.CreateAsync(Rule("R01", Today, ("LOW1", null, 100)));
    }

    [Fact]
    public async Task First_version_may_start_any_day()
    {
        var (_, service) = await CreateAsync();

        await service.CreateAsync(Rule("R01", Today.AddDays(-30), ("CC01", null, 100)));
    }

    [Fact]
    public async Task Current_or_past_version_is_immutable()
    {
        var (_, service) = await CreateAsync();
        await service.CreateAsync(Rule("R01", Today, ("CC01", null, 60), ("CC02", null, 40)));

        Assert.Equal(
            "A versão vigente em 21/10/2026 já está em uso e não pode ser alterada; crie uma versão com nova data.",
            await Throws(() => service.SaveVersionAsync("R01", Version(Today, ("CC01", null, 50), ("CC02", null, 50)), Today)));
    }

    [Fact]
    public async Task Future_version_can_be_replaced_and_deleted()
    {
        var (db, service) = await CreateAsync();
        await service.CreateAsync(Rule("R01", Today, ("CC01", null, 60), ("CC02", null, 40)));
        var tomorrow = Today.AddDays(1);

        await service.SaveVersionAsync("R01", Version(tomorrow, ("CC01", "IT01", 70), ("CC02", null, 30)), Today);
        var replaced = await service.SaveVersionAsync("r01", Version(tomorrow, (null, "IT01", 50), ("CC02", null, 50)), Today);

        Assert.Equal(2, await db.Context.DistributionRuleVersions.CountAsync());
        Assert.Equal(2, await db.Context.DistributionRuleLines.CountAsync(l => l.VersionId == replaced.Id));

        await service.DeleteVersionAsync(replaced.Id, Today);

        Assert.Single(await db.Context.DistributionRuleVersions.ToListAsync());
        Assert.Equal(2, await db.Context.DistributionRuleLines.CountAsync());
    }

    [Fact]
    public async Task Today_or_past_new_version_is_rejected()
    {
        var (_, service) = await CreateAsync();
        await service.CreateAsync(Rule("R01", Today.AddDays(-10), ("CC01", null, 100)));

        foreach (var day in new[] { Today, Today.AddDays(-5) })
        {
            Assert.Equal("A nova versão deve começar depois de hoje; versões passadas não podem ser criadas.",
                await Throws(() => service.SaveVersionAsync("R01", Version(day, ("CC02", null, 100)), Today)));
        }
    }

    [Fact]
    public async Task Current_version_cannot_be_deleted()
    {
        var (_, service) = await CreateAsync();
        var rule = await service.CreateAsync(Rule("R01", Today, ("CC01", null, 100)));

        Assert.Equal("A versão vigente em 21/10/2026 já está em uso e não pode ser excluída.",
            await Throws(() => service.DeleteVersionAsync(rule.Versions[0].Id, Today)));
    }

    [Fact]
    public async Task Last_version_cannot_be_deleted()
    {
        var (_, service) = await CreateAsync();
        var rule = await service.CreateAsync(Rule("R01", Today.AddDays(5), ("CC01", null, 100)));

        Assert.Equal("A regra precisa ter ao menos uma versão.",
            await Throws(() => service.DeleteVersionAsync(rule.Versions[0].Id, Today)));
    }

    [Fact]
    public async Task UpdateHeader_changes_name_and_inactive()
    {
        var (_, service) = await CreateAsync();
        await service.CreateAsync(Rule("R01", Today, ("CC01", null, 100)));

        var updated = await service.UpdateHeaderAsync("r01", "Novo nome", true);

        Assert.Equal("Novo nome", updated!.Name);
        Assert.True(updated.Inactive);
        Assert.Null(await service.UpdateHeaderAsync("ZZ", "x", false));
    }
}
