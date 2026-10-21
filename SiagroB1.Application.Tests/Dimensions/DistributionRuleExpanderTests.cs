using SiagroB1.Application.Services.Dimensions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Dimensions;

public class DistributionRuleExpanderTests
{
    private static async Task<(UnitOfWork Db, DistributionRuleExpander Expander)> CreateAsync()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.CostCenters.AddRange(
            new CostCenter { Code = "CC01", Name = "A", DimensionNumber = 1 },
            new CostCenter { Code = "CC02", Name = "B", DimensionNumber = 1 });
        db.Context.DistributionRules.Add(new DistributionRule
        {
            Code = "R01",
            Name = "Rateio",
            DimensionNumber = 1,
            Versions =
            [
                NewVersion(new DateTime(2026, 10, 1), 60, 40),
                NewVersion(new DateTime(2026, 10, 22), 50, 50),
            ],
        });
        await db.SaveChangesAsync();
        return (db, new DistributionRuleExpander(db));
    }

    private static DistributionRuleVersion NewVersion(DateTime validFrom, decimal first, decimal second) =>
        new()
        {
            RuleCode = "R01",
            ValidFrom = validFrom,
            Lines =
            [
                new DistributionRuleLine { CostCenterCode = "CC01", Percentage = first },
                new DistributionRuleLine { CostCenterCode = "CC02", Percentage = second },
            ],
        };

    [Fact]
    public async Task Center_expands_to_itself()
    {
        var (_, expander) = await CreateAsync();

        var result = await expander.ExpandAsync(1, "CC01", new DateTime(2026, 10, 21));

        Assert.Equal([("CC01", 100m)], result);
    }

    [Fact]
    public async Task Uses_version_valid_at_document_date()
    {
        var (_, expander) = await CreateAsync();

        var before = await expander.ExpandAsync(1, "R01", new DateTime(2026, 10, 21, 15, 30, 0));
        var after = await expander.ExpandAsync(1, "R01", new DateTime(2026, 10, 22));

        Assert.Equal([("CC01", 60m), ("CC02", 40m)], before);
        Assert.Equal([("CC01", 50m), ("CC02", 50m)], after);
    }

    [Fact]
    public async Task No_version_before_first_ValidFrom_throws()
    {
        var (_, expander) = await CreateAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            expander.ExpandAsync(1, "R01", new DateTime(2026, 9, 30)));

        Assert.Equal("A regra R01 não tem versão vigente em 30/09/2026.", ex.Message);
    }

    [Fact]
    public async Task Unknown_code_throws()
    {
        var (_, expander) = await CreateAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            expander.ExpandAsync(1, "ZZ99", new DateTime(2026, 10, 21)));

        Assert.Equal("O código ZZ99 não existe na dimensão 1.", ex.Message);
    }
}
