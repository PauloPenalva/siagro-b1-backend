using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Taxes;

/// <summary>
/// A regra única: STANDALONE + chave ligada na filial. A contraprova obrigatória é SAPB1 com a
/// chave gravada à mão no banco — tem de continuar inativa.
/// </summary>
public class TaxCalculationGateTests
{
    private static async Task<UnitOfWork> DbWithBranch(bool issuesNfe)
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.Branchs.Add(new Branch
        {
            Code = "01", BranchName = "MATRIZ", StateCode = "SP", TaxRegime = TaxRegime.Normal, IssuesNfe = issuesNfe,
        });
        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task Standalone_with_switch_on_is_active() =>
        Assert.True(await TaxTestServices.Gate(await DbWithBranch(true), "STANDALONE").IsActiveAsync("01"));

    [Fact]
    public async Task Missing_erp_key_counts_as_standalone() =>
        Assert.True(await TaxTestServices.Gate(await DbWithBranch(true), null).IsActiveAsync("01"));

    [Fact]
    public async Task Standalone_with_switch_off_is_inactive() =>
        Assert.False(await TaxTestServices.Gate(await DbWithBranch(false), "STANDALONE").IsActiveAsync("01"));

    [Fact]
    public async Task Sapb1_with_switch_forced_on_is_inactive() =>
        Assert.False(await TaxTestServices.Gate(await DbWithBranch(true), "SAPB1").IsActiveAsync("01"));

    [Fact]
    public async Task Any_other_mode_is_inactive() =>
        Assert.False(await TaxTestServices.Gate(await DbWithBranch(true), "PROTHEUS").IsActiveAsync("01"));

    [Fact]
    public async Task Unknown_or_blank_branch_is_inactive()
    {
        var gate = TaxTestServices.Gate(await DbWithBranch(true), "STANDALONE");
        Assert.False(await gate.IsActiveAsync("99"));
        Assert.False(await gate.IsActiveAsync(null));
        Assert.False(await gate.IsActiveAsync(" "));
    }
}
