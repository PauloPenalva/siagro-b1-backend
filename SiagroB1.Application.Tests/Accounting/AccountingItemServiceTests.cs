using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.Accounting;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;

namespace SiagroB1.Application.Tests.Accounting;

public class AccountingItemServiceTests
{
    private static AccountingItemService Create(string name) =>
        new(TestDb.CreateUnitOfWork(name), NullLogger<AccountingItemService>.Instance);

    private static AccountingItemModel Model(string code, AccountingEntityClass cls = AccountingEntityClass.Analytic,
        string? parent = null) =>
        new() { Code = code, Name = "Nome", Class = cls, ParentCode = parent };

    private static async Task<string> Throws(Func<Task> act) =>
        (await Assert.ThrowsAsync<DefaultException>(act)).Message;

    [Fact]
    public async Task Create_update_delete_roundtrip_with_normalization()
    {
        var name = Guid.NewGuid().ToString();
        var svc = Create(name);

        var created = await svc.CreateAsync(Model(" cam01 "));
        Assert.Equal("CAM01", created.Code);

        var read = await Create(name).GetByIdAsync("CAM01");
        Assert.Equal("Nome", read!.Name);

        var upd = Model("CAM01");
        upd.Name = "Novo";
        upd.Inactive = true;
        var updated = await svc.UpdateAsync("CAM01", upd);
        Assert.Equal("Novo", updated!.Name);
        Assert.True((await Create(name).GetByIdAsync("CAM01"))!.Inactive);

        Assert.True(await svc.DeleteAsync("CAM01"));
        Assert.Null(await Create(name).GetByIdAsync("CAM01"));
        Assert.False(await svc.DeleteAsync("CAM01"));
    }

    [Fact]
    public async Task Rejects_duplicate_code()
    {
        var svc = Create(Guid.NewGuid().ToString());
        await svc.CreateAsync(Model("A"));
        await Throws(() => svc.CreateAsync(Model("a")));
    }

    [Fact]
    public async Task Hierarchy_rules_apply()
    {
        var svc = Create(Guid.NewGuid().ToString());
        Assert.Equal("O superior X não existe.", await Throws(() => svc.CreateAsync(Model("A", parent: "x"))));
        await svc.CreateAsync(Model("P"));
        Assert.Equal("O superior precisa ser sintético.", await Throws(() => svc.CreateAsync(Model("A", parent: "P"))));
        await svc.CreateAsync(Model("S", AccountingEntityClass.Synthetic));
        await svc.CreateAsync(Model("F", parent: "S"));
        Assert.Equal("Não é possível tornar analítico: S tem subordinados.",
            await Throws(() => svc.UpdateAsync("S", Model("S"))));
        Assert.Equal("Não é possível excluir: S tem subordinados.", await Throws(() => svc.DeleteAsync("S")));
        Assert.Equal("O código não pode ser superior dele mesmo.",
            await Throws(() => svc.UpdateAsync("S", Model("S", AccountingEntityClass.Synthetic, "S"))));
    }

    [Fact]
    public async Task Rejects_validity_to_before_from()
    {
        var svc = Create(Guid.NewGuid().ToString());
        var m = Model("A");
        m.ValidFrom = new DateTime(2026, 5, 1);
        m.ValidTo = new DateTime(2026, 4, 1);
        Assert.Equal("A vigência final deve ser posterior à inicial.", await Throws(() => svc.CreateAsync(m)));
    }
}
