using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.WarehouseReconciliations;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.WarehouseReconciliations;

/// <summary>
/// O guard "no máximo uma aberta" (<see cref="WarehouseReconciliationsGuardService.HasOpenAsync"/>)
/// consulta o banco ANTES do SaveChanges: duas criações/edições simultâneas para o mesmo
/// armazém+produto podem passar as duas pelo guard e só colidir no índice único filtrado
/// (<c>IX_WAREHOUSE_RECONCILIATIONS_OpenPerWarehouseItem</c>), que o InMemory não aplica. Estes
/// testes usam um <see cref="ThrowOnSaveInterceptor"/> para simular essa colisão no SaveChanges
/// real e verificar que ela vira a mesma mensagem de negócio do guard, não um 500 cru.
/// </summary>
public class WarehouseReconciliationsConcurrencyTests
{
    private static WarehouseReconciliationsDescriptionService Descriptions() => new(
        new FakeItemService(new() { [WarehouseReconciliationsTestContext.Item] = "SOJA EM GRAOS" }),
        new FakeWarehouseService(new() { [WarehouseReconciliationsTestContext.ThirdPartyWarehouse] = "Armazém Terceiro" }),
        new FakeBusinessPartnerService(new() { [WarehouseReconciliationsTestContext.ThirdPartyWarehouse] = "Armazém Terceiro Ltda" }));

    [Fact]
    public async Task Create_translates_a_concurrent_unique_index_violation_into_the_already_open_message()
    {
        var db = TestDb.CreateUnitOfWork(new ThrowOnSaveInterceptor(
            e => e.Entity is WarehouseReconciliation && e.State == EntityState.Added));
        var resource = new FakeStringLocalizer<Resource>();

        var reason = new WarehouseReconciliationReason
        {
            Key = Guid.NewGuid(), Code = "R1", Description = "Quebra técnica", Active = true,
        };
        db.Context.WarehouseReconciliationReasons.Add(reason);
        await db.Context.SaveChangesAsync(); // sem WarehouseReconciliation Added: não aciona o interceptor

        var create = new WarehouseReconciliationsCreateService(
            db,
            new FakeDocNumberSequenceService(),
            Descriptions(),
            new WarehouseReconciliationsGuardService(db, new WarehouseComplementService(db), resource),
            resource);

        var r = WarehouseReconciliationsTestContext.NewReconciliation(reason.Key, 10m);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => create.ExecuteAsync(r, "tester"));

        Assert.Equal("WAREHOUSE_RECONCILIATION_ALREADY_OPEN", ex.Message);
    }

    [Fact]
    public async Task Update_translates_a_concurrent_unique_index_violation_into_the_already_open_message()
    {
        var db = TestDb.CreateUnitOfWork(new ThrowOnSaveInterceptor(
            e => e.Entity is WarehouseReconciliation && e.State == EntityState.Modified));
        var resource = new FakeStringLocalizer<Resource>();

        var reason = new WarehouseReconciliationReason
        {
            Key = Guid.NewGuid(), Code = "R1", Description = "Quebra técnica", Active = true,
        };
        db.Context.WarehouseReconciliationReasons.Add(reason);

        var existing = WarehouseReconciliationsTestContext.NewReconciliation(reason.Key, 50m);
        existing.Key = Guid.NewGuid();
        existing.Status = WarehouseReconciliationStatus.Draft;
        db.Context.WarehouseReconciliations.Add(existing);
        await db.Context.SaveChangesAsync(); // Added, não Modified: não aciona o interceptor

        var update = new WarehouseReconciliationsUpdateService(
            db,
            Descriptions(),
            new WarehouseReconciliationsGuardService(db, new WarehouseComplementService(db), resource),
            resource);

        var input = WarehouseReconciliationsTestContext.NewReconciliation(reason.Key, 60m);

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => update.ExecuteAsync(existing.Key, input, "editor"));

        Assert.Equal("WAREHOUSE_RECONCILIATION_ALREADY_OPEN", ex.Message);
    }
}
