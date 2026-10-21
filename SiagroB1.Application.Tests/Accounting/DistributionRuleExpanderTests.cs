using SiagroB1.Application.Services.Accounting;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Exceptions;
using static SiagroB1.Application.Tests.Accounting.AccountingTestData;

namespace SiagroB1.Application.Tests.Accounting;

public class DistributionRuleExpanderTests
{
    private static async Task<DistributionRuleExpander> CreateAsync()
    {
        var db = TestDb.CreateUnitOfWork();
        var rule = Rule("R01", new DateTime(2026, 10, 1), ("CC01", "IT01", 60), (null, "IT02", 40));
        rule.Versions.Add(Version(new DateTime(2026, 10, 22), ("CC01", null, 50), ("CC02", null, 50)));
        await AddRuleAsync(db, rule);
        return new DistributionRuleExpander(db);
    }

    [Fact]
    public async Task Uses_version_valid_at_document_date()
    {
        var expander = await CreateAsync();

        var before = await expander.ExpandAsync("r01", new DateTime(2026, 10, 21, 15, 30, 0));
        var after = await expander.ExpandAsync("R01", new DateTime(2026, 10, 22));

        Assert.Equal([("CC01", "IT01", 60m), (null, "IT02", 40m)], before);
        Assert.Equal([("CC01", null, 50m), ("CC02", null, 50m)], after);
    }

    [Fact]
    public async Task No_version_before_first_ValidFrom_throws()
    {
        var expander = await CreateAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => expander.ExpandAsync("R01", new DateTime(2026, 9, 30)));

        Assert.Equal("O rateio R01 não tem versão vigente em 30/09/2026.", ex.Message);
    }

    [Fact]
    public async Task Unknown_code_throws()
    {
        var expander = await CreateAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => expander.ExpandAsync("ZZ99", Today));

        Assert.Equal("O rateio ZZ99 não existe.", ex.Message);
    }
}
