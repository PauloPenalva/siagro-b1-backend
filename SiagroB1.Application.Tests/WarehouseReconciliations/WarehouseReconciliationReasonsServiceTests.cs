using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.WarehouseReconciliations;

public class WarehouseReconciliationReasonsServiceTests
{
    private readonly WarehouseReconciliationsTestContext _ctx = new();

    [Fact]
    public async Task Create_normalizes_code_and_starts_active()
    {
        var reason = new WarehouseReconciliationReason { Code = " quebra ", Description = "Quebra técnica" };

        await _ctx.ReasonsCreate().ExecuteAsync(reason, "tester");

        var saved = await _ctx.Db.Context.WarehouseReconciliationReasons.AsNoTracking().SingleAsync();
        Assert.Equal("QUEBRA", saved.Code);
        Assert.True(saved.Active);
        Assert.Equal("tester", saved.CreatedBy);
    }

    [Fact]
    public async Task Create_refuses_duplicate_code()
    {
        await _ctx.SeedReasonAsync(code: "QUEBRA");

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => _ctx.ReasonsCreate().ExecuteAsync(
            new WarehouseReconciliationReason { Code = "quebra", Description = "Outra" }, "tester"));

        Assert.Equal("WAREHOUSE_RECONCILIATION_REASON_CODE_DUPLICATE", ex.Message);
    }

    [Fact]
    public async Task Create_refuses_blank_description()
    {
        var ex = await Assert.ThrowsAsync<ApplicationException>(() => _ctx.ReasonsCreate().ExecuteAsync(
            new WarehouseReconciliationReason { Code = "X", Description = " " }, "tester"));

        Assert.Equal("WAREHOUSE_RECONCILIATION_REASON_REQUIRED_FIELDS", ex.Message);
    }

    [Fact]
    public async Task Update_can_deactivate()
    {
        var reason = await _ctx.SeedReasonAsync(code: "QUEBRA");

        await _ctx.ReasonsUpdate().ExecuteAsync(reason.Key,
            new WarehouseReconciliationReason { Code = "QUEBRA", Description = "Quebra", Active = false }, "tester");

        var saved = await _ctx.Db.Context.WarehouseReconciliationReasons.AsNoTracking().SingleAsync();
        Assert.False(saved.Active);
    }

    [Fact]
    public async Task Update_refuses_code_of_another_reason()
    {
        await _ctx.SeedReasonAsync(code: "A");
        var b = await _ctx.SeedReasonAsync(code: "B");

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => _ctx.ReasonsUpdate().ExecuteAsync(b.Key,
            new WarehouseReconciliationReason { Code = "A", Description = "B", Active = true }, "tester"));

        Assert.Equal("WAREHOUSE_RECONCILIATION_REASON_CODE_DUPLICATE", ex.Message);
    }

    [Fact]
    public async Task Delete_refuses_reason_in_use()
    {
        var reason = await _ctx.SeedReasonAsync();
        var reconciliation = WarehouseReconciliationsTestContext.NewReconciliation(reason.Key, 10m);
        reconciliation.Key = Guid.NewGuid();
        _ctx.Db.Context.WarehouseReconciliations.Add(reconciliation);
        await _ctx.Db.Context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => _ctx.ReasonsDelete().ExecuteAsync(reason.Key));

        Assert.Equal("WAREHOUSE_RECONCILIATION_REASON_IN_USE", ex.Message);
    }

    [Fact]
    public async Task Delete_removes_unused_reason()
    {
        var reason = await _ctx.SeedReasonAsync();

        await _ctx.ReasonsDelete().ExecuteAsync(reason.Key);

        Assert.False(await _ctx.Db.Context.WarehouseReconciliationReasons.AnyAsync());
    }
}
