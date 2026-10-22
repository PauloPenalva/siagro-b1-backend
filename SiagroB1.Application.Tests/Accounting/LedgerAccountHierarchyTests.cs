using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.Accounting;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;

namespace SiagroB1.Application.Tests.Accounting;

public class LedgerAccountHierarchyTests
{
    private static LedgerAccountService Create(string name) =>
        new(TestDb.CreateUnitOfWork(name), NullLogger<LedgerAccountService>.Instance);

    private static LedgerAccountModel Model(string code, LedgerAccountType type, bool analytic, string? parent = null) => new()
    {
        Code = code, Name = code, Type = type, AllowsPosting = analytic, ParentCode = parent,
    };

    [Fact]
    public async Task Rejects_parent_cycle()
    {
        var name = Guid.NewGuid().ToString();
        var svc = Create(name);
        await svc.CreateAsync(Model("1", LedgerAccountType.Asset, false));
        await svc.CreateAsync(Model("1.1", LedgerAccountType.Asset, false, "1"));

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            Create(name).UpdateAsync("1", Model("1", LedgerAccountType.Asset, false, "1.1")));
        Assert.Equal("A conta superior 1.1 geraria um ciclo na hierarquia.", ex.Message);
    }

    [Fact]
    public async Task Rejects_self_as_parent()
    {
        var name = Guid.NewGuid().ToString();
        await Create(name).CreateAsync(Model("1", LedgerAccountType.Asset, false));
        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            Create(name).UpdateAsync("1", Model("1", LedgerAccountType.Asset, false, "1")));
        Assert.Equal("A conta superior 1 geraria um ciclo na hierarquia.", ex.Message);
    }

    [Fact]
    public async Task Rejects_analytic_parent()
    {
        var name = Guid.NewGuid().ToString();
        await Create(name).CreateAsync(Model("1.1.01", LedgerAccountType.Asset, true));
        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            Create(name).CreateAsync(Model("1.1.01.1", LedgerAccountType.Asset, true, "1.1.01")));
        Assert.Equal("A conta superior 1.1.01 precisa ser sintética.", ex.Message);
    }

    [Fact]
    public async Task Rejects_missing_parent()
    {
        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            Create(Guid.NewGuid().ToString()).CreateAsync(Model("1.1", LedgerAccountType.Asset, false, "9")));
        Assert.Equal("Conta superior 9 não encontrada.", ex.Message);
    }

    [Fact]
    public async Task Rejects_turning_synthetic_with_children_into_analytic()
    {
        var name = Guid.NewGuid().ToString();
        await Create(name).CreateAsync(Model("1", LedgerAccountType.Asset, false));
        await Create(name).CreateAsync(Model("1.1", LedgerAccountType.Asset, true, "1"));
        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            Create(name).UpdateAsync("1", Model("1", LedgerAccountType.Asset, true)));
        Assert.Equal("A conta 1 tem contas inferiores e precisa continuar sintética.", ex.Message);
    }

    [Theory]
    [InlineData(LedgerAccountType.Asset, LedgerAccountNature.Debit)]
    [InlineData(LedgerAccountType.Expense, LedgerAccountNature.Debit)]
    [InlineData(LedgerAccountType.Liability, LedgerAccountNature.Credit)]
    [InlineData(LedgerAccountType.Equity, LedgerAccountNature.Credit)]
    [InlineData(LedgerAccountType.Revenue, LedgerAccountNature.Credit)]
    public async Task Nature_defaults_from_type(LedgerAccountType type, LedgerAccountNature expected)
    {
        var name = Guid.NewGuid().ToString();
        await Create(name).CreateAsync(Model("9", type, true));
        var read = await Create(name).GetByIdAsync("9");
        Assert.Equal(expected, read!.Nature);
    }

    [Fact]
    public async Task Explicit_nature_is_kept()
    {
        var name = Guid.NewGuid().ToString();
        var m = Model("9", LedgerAccountType.Asset, true);
        m.Nature = LedgerAccountNature.Credit; // retificadora do ativo
        await Create(name).CreateAsync(m);
        Assert.Equal(LedgerAccountNature.Credit, (await Create(name).GetByIdAsync("9"))!.Nature);
    }

    [Fact]
    public async Task ResultClosingAccount_only_on_revenue_or_expense()
    {
        var name = Guid.NewGuid().ToString();
        await Create(name).CreateAsync(Model("2.4.01", LedgerAccountType.Equity, true));
        var m = Model("1.1.01", LedgerAccountType.Asset, true);
        m.ResultClosingAccountCode = "2.4.01";
        var ex = await Assert.ThrowsAsync<DefaultException>(() => Create(name).CreateAsync(m));
        Assert.Equal("Conta de apuração só se aplica a contas de receita ou despesa.", ex.Message);

        var r = Model("3.1.01", LedgerAccountType.Revenue, true);
        r.ResultClosingAccountCode = "2.4.01";
        await Create(name).CreateAsync(r);
        Assert.Equal("2.4.01", (await Create(name).GetByIdAsync("3.1.01"))!.ResultClosingAccountCode);
    }

    [Fact]
    public void IsPostable_false_inside_block_range()
    {
        var a = new LedgerAccount
        {
            Code = "1", Name = "x", AllowsPosting = true,
            BlockedFrom = new DateTime(2026, 10, 1), BlockedTo = new DateTime(2026, 10, 31),
        };
        Assert.False(LedgerAccountRules.IsPostable(a, new DateTime(2026, 10, 15)));
        Assert.True(LedgerAccountRules.IsPostable(a, new DateTime(2026, 11, 1)));
        a.BlockedTo = null;
        Assert.False(LedgerAccountRules.IsPostable(a, new DateTime(2027, 1, 1)));
        Assert.False(LedgerAccountRules.IsPostable(new LedgerAccount { Code = "2", Name = "s", AllowsPosting = false }, DateTime.Today));
        Assert.False(LedgerAccountRules.IsPostable(new LedgerAccount { Code = "3", Name = "i", AllowsPosting = true, Inactive = true }, DateTime.Today));
    }
}
