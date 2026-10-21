using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;

namespace SiagroB1.Application.Tests.Accounting;

public class LedgerAccountFlagsTests
{
    private static LedgerAccountService Create(string name) =>
        new(TestDb.CreateUnitOfWork(name), NullLogger<LedgerAccountService>.Instance);

    private static LedgerAccountModel Model(bool aCc, bool rCc, bool aIt, bool rIt) => new()
    {
        Code = "4.1.1", Name = "Despesa", Type = LedgerAccountType.Expense, AllowsPosting = true,
        AcceptsCostCenter = aCc, RequiresCostCenter = rCc, AcceptsAccountingItem = aIt, RequiresAccountingItem = rIt,
    };

    [Fact]
    public async Task Flags_roundtrip()
    {
        var name = Guid.NewGuid().ToString();
        await Create(name).CreateAsync(Model(true, true, true, false));
        var read = await Create(name).GetByIdAsync("4.1.1");
        Assert.True(read!.AcceptsCostCenter);
        Assert.True(read.RequiresCostCenter);
        Assert.True(read.AcceptsAccountingItem);
        Assert.False(read.RequiresAccountingItem);
    }

    [Fact]
    public async Task Update_changes_flags()
    {
        var name = Guid.NewGuid().ToString();
        await Create(name).CreateAsync(Model(false, false, false, false));
        await Create(name).UpdateAsync("4.1.1", Model(true, true, true, true));
        var read = await Create(name).GetByIdAsync("4.1.1");
        Assert.True(read!.RequiresCostCenter && read.RequiresAccountingItem);
    }

    [Fact]
    public async Task Rejects_requires_without_accepts_cost_center()
    {
        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            Create(Guid.NewGuid().ToString()).CreateAsync(Model(false, true, true, false)));
        Assert.Equal("A conta não pode exigir e não aceitar centro de custo.", ex.Message);
    }

    [Fact]
    public async Task Rejects_requires_without_accepts_item()
    {
        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            Create(Guid.NewGuid().ToString()).CreateAsync(Model(true, false, false, true)));
        Assert.Equal("A conta não pode exigir e não aceitar item contábil.", ex.Message);
    }
}
