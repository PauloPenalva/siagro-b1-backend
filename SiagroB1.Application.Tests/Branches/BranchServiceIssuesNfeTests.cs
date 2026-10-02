using SiagroB1.Application.Services;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Branches;

/// <summary>
/// Com a chave "Emite NF-e pelo Siagro" ligada, CRT e UF são obrigatórios — só em STANDALONE.
/// Em SAPB1 a chave é oculta e ignorada, então não pode travar o cadastro.
/// </summary>
public class BranchServiceIssuesNfeTests
{
    private static BranchService Service(UnitOfWork db, string erp) =>
        new(db.Context, TaxTestServices.Config(erp));

    private static Branch NewBranch(bool issuesNfe, TaxRegime? regime, string? state) => new()
    {
        Code = "01", BranchName = "MATRIZ", ShortName = "MTZ", TaxId = "68583898000101",
        IssuesNfe = issuesNfe, TaxRegime = regime, StateCode = state,
    };

    [Fact]
    public async Task Standalone_rejects_issuing_nfe_without_tax_regime()
    {
        var db = TestDb.CreateUnitOfWork();
        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            Service(db, "STANDALONE").CreateAsync(NewBranch(true, null, "SP")));
        Assert.Contains("regime tributário", ex.Message);
    }

    [Fact]
    public async Task Standalone_rejects_issuing_nfe_without_state()
    {
        var db = TestDb.CreateUnitOfWork();
        await Assert.ThrowsAsync<DefaultException>(() =>
            Service(db, "STANDALONE").CreateAsync(NewBranch(true, TaxRegime.Normal, null)));
    }

    [Fact]
    public async Task Standalone_accepts_issuing_nfe_with_regime_and_state()
    {
        var db = TestDb.CreateUnitOfWork();
        var created = await Service(db, "STANDALONE").CreateAsync(NewBranch(true, TaxRegime.Normal, "SP"));
        Assert.True(created.IssuesNfe);
    }

    [Fact]
    public async Task Standalone_update_cannot_clear_the_regime_while_issuing_nfe()
    {
        var db = TestDb.CreateUnitOfWork();
        var created = await Service(db, "STANDALONE").CreateAsync(NewBranch(true, TaxRegime.Normal, "SP"));

        created.TaxRegime = null;

        await Assert.ThrowsAsync<DefaultException>(() => Service(db, "STANDALONE").UpdateAsync("01", created));
    }

    [Fact]
    public async Task Turning_the_switch_off_is_always_allowed()
    {
        var db = TestDb.CreateUnitOfWork();
        var created = await Service(db, "STANDALONE").CreateAsync(NewBranch(true, TaxRegime.Normal, "SP"));

        created.IssuesNfe = false;
        created.TaxRegime = null;

        var updated = await Service(db, "STANDALONE").UpdateAsync("01", created);
        Assert.False(updated!.IssuesNfe);
    }

    [Fact]
    public async Task Sapb1_ignores_the_switch()
    {
        var db = TestDb.CreateUnitOfWork();
        var created = await Service(db, "SAPB1").CreateAsync(NewBranch(true, null, null));
        Assert.True(created.IssuesNfe);
    }
}
