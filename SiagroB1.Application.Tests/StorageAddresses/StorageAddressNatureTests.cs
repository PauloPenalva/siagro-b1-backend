using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.StorageAddresses;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.StorageAddresses;

/// <summary>
/// GAC-1181 fase 2, Task 1: o lote ganha uma natureza (<see cref="StorageAddressNature"/>),
/// escolhida na criação e imutável depois. "Transbordo" só é aceita em armazém próprio
/// (<see cref="Domain.Entities.WarehouseComplement.IsOwn"/>) — ver
/// docs/superpowers/specs/2026-09-21-gac-1181-load-transshipment-design.md, seção "Fase 2", item 3.
/// </summary>
public class StorageAddressNatureTests
{
    private static StorageAddress NewStorageAddress(string warehouseCode, StorageAddressNature nature) => new()
    {
        Description = "Lote teste",
        CardCode = "C0001",
        ItemCode = "SOJA",
        WarehouseCode = warehouseCode,
        UoM = "KG",
        Nature = nature,
    };

    private static StorageAddressesCreateService CreateServiceOver(IUnitOfWork db) => new(
        db,
        new FakeDocNumberSequenceService(),
        new FakeBusinessPartnerService(),
        new FakeItemService(),
        new FakeWarehouseService(),
        new WarehouseComplementService(db),
        new TestLogger<StorageAddressesCreateService>());

    [Fact]
    public void Nature_DefaultsToRegular()
    {
        var entity = new StorageAddress
        {
            Description = "Lote teste",
            CardCode = "C0001",
            ItemCode = "SOJA",
            WarehouseCode = "ARM01",
            UoM = "KG",
        };

        Assert.Equal(StorageAddressNature.Regular, entity.Nature);
    }

    /// <summary>
    /// A ausência de linha em WAREHOUSE_COMPLEMENTS já é "não é próprio" (outro teste cobriria
    /// esse ramo) — este grava a linha explicitamente com IsOwn = false, o caso comum de armazém
    /// cadastrado como participante mas de terceiro.
    /// </summary>
    [Fact]
    public async Task Create_RefusesTransshipmentNatureOnThirdPartyWarehouse()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.WarehouseComplements.Add(new WarehouseComplement { WarehouseCode = "F010966", IsOwn = false });
        await db.Context.SaveChangesAsync();

        var service = CreateServiceOver(db);
        var entity = NewStorageAddress("F010966", StorageAddressNature.Transshipment);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => service.ExecuteAsync(entity, "tester"));

        Assert.Contains("F010966", ex.Message);
    }

    [Fact]
    public async Task Create_AcceptsTransshipmentNatureOnOwnWarehouse()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.WarehouseComplements.Add(new WarehouseComplement { WarehouseCode = "ARM01", IsOwn = true });
        await db.Context.SaveChangesAsync();

        var service = CreateServiceOver(db);
        var entity = NewStorageAddress("ARM01", StorageAddressNature.Transshipment);

        var created = await service.ExecuteAsync(entity, "tester");

        Assert.Equal(StorageAddressNature.Transshipment, created.Nature);
    }

    /// <summary>
    /// Reproduz a armadilha do fluxo OData: no PATCH, a entidade que o controller passa ao
    /// serviço é a MESMA instância rastreada que o service volta a buscar do contexto (identity
    /// map), já com o valor novo aplicado por <c>Delta.Patch</c>. Comparar
    /// <c>existingAddress.Nature</c> com <c>entity.Nature</c> nunca acusaria a mudança, porque são
    /// o mesmo objeto — só <c>OriginalValues</c> ainda guarda o valor persistido.
    /// </summary>
    [Fact]
    public async Task Update_RefusesChangingTheNature()
    {
        var db = TestDb.CreateUnitOfWork();

        var seed = new StorageAddress
        {
            Code = "L0001",
            Description = "Lote de transbordo",
            CardCode = "C0001",
            ItemCode = "SOJA",
            WarehouseCode = "ARM01",
            UoM = "KG",
            Nature = StorageAddressNature.Transshipment,
            TransactionOrigin = TransactionCode.StorageAddress,
        };

        db.Context.StorageAddresses.Add(seed);
        await db.Context.SaveChangesAsync();

        // O mesmo objeto seed: a query abaixo só o devolve de novo pelo identity map do
        // EF, sem reler o banco — igual ao que StorageAddressesController.PatchAsync faz ao
        // buscar a entidade e aplicar o Delta nela antes de chamar o serviço.
        var tracked = await db.Context.StorageAddresses.SingleAsync(x => x.Code == "L0001");
        tracked.Nature = StorageAddressNature.Regular;

        var service = new StorageAddressesUpdateService(
            db.Context,
            new FakeBusinessPartnerService(),
            new FakeItemService(),
            new FakeWarehouseService(),
            new TestLogger<StorageAddressesUpdateService>());

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => service.ExecuteAsync("L0001", tracked, "tester"));

        Assert.Contains("L0001", ex.Message);
    }
}
