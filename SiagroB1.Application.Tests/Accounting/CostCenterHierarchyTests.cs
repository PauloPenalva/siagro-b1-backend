using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;

namespace SiagroB1.Application.Tests.Accounting;

public class CostCenterHierarchyTests
{
    private static CostCenterService Create(string name) =>
        new(TestDb.CreateUnitOfWork(name), NullLogger<CostCenterService>.Instance);

    private static CostCenterModel Model(string code, AccountingEntityClass cls = AccountingEntityClass.Analytic,
        string? parent = null) =>
        new() { Code = code, Name = code, Class = cls, ParentCode = parent };

    private static async Task<string> Throws(Func<Task> act) =>
        (await Assert.ThrowsAsync<DefaultException>(act)).Message;

    [Fact]
    public async Task Normalizes_code_and_roundtrips_fields()
    {
        var name = Guid.NewGuid().ToString();
        var m = Model(" cam01 ");
        m.ValidFrom = new DateTime(2026, 1, 1);
        m.ValidTo = new DateTime(2026, 12, 31);
        await Create(name).CreateAsync(m);

        var read = await Create(name).GetByIdAsync("CAM01");
        Assert.NotNull(read);
        Assert.Equal(AccountingEntityClass.Analytic, read!.Class);
        Assert.Equal(new DateTime(2026, 12, 31), read.ValidTo);
    }

    [Fact]
    public async Task Rejects_missing_parent()
    {
        var svc = Create(Guid.NewGuid().ToString());
        Assert.Equal("O superior PAI não existe.", await Throws(() => svc.CreateAsync(Model("A", parent: "pai"))));
    }

    [Fact]
    public async Task Rejects_analytic_parent()
    {
        var svc = Create(Guid.NewGuid().ToString());
        await svc.CreateAsync(Model("P"));
        Assert.Equal("O superior precisa ser sintético.", await Throws(() => svc.CreateAsync(Model("A", parent: "P"))));
    }

    [Fact]
    public async Task Rejects_self_parent_and_cycle()
    {
        var svc = Create(Guid.NewGuid().ToString());
        await svc.CreateAsync(Model("A", AccountingEntityClass.Synthetic));
        await svc.CreateAsync(Model("B", AccountingEntityClass.Synthetic, "A"));
        await svc.CreateAsync(Model("C", AccountingEntityClass.Synthetic, "B"));

        Assert.Equal("O código não pode ser superior dele mesmo.",
            await Throws(() => svc.UpdateAsync("A", Model("A", AccountingEntityClass.Synthetic, "A"))));
        Assert.Equal("O código não pode ser superior dele mesmo.",
            await Throws(() => svc.UpdateAsync("A", Model("A", AccountingEntityClass.Synthetic, "C"))));
    }

    [Fact]
    public async Task Rejects_making_analytic_with_children_and_delete_with_children()
    {
        var svc = Create(Guid.NewGuid().ToString());
        await svc.CreateAsync(Model("A", AccountingEntityClass.Synthetic));
        await svc.CreateAsync(Model("B", parent: "A"));

        Assert.Equal("Não é possível tornar analítico: A tem subordinados.",
            await Throws(() => svc.UpdateAsync("A", Model("A"))));
        Assert.Equal("Não é possível excluir: A tem subordinados.", await Throws(() => svc.DeleteAsync("A")));

        Assert.True(await svc.DeleteAsync("B"));
        Assert.True(await svc.DeleteAsync("A"));
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

    [Fact]
    public async Task Rejects_code_longer_than_20()
    {
        var svc = Create(Guid.NewGuid().ToString());
        await Throws(() => svc.CreateAsync(Model(new string('X', 21))));
    }
}
