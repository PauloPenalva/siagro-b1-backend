using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Dimensions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Dimensions;

public class AccountingDimensionServiceTests
{
    private static async Task<(UnitOfWork Db, AccountingDimensionService Service)> CreateAsync()
    {
        var db = TestDb.CreateUnitOfWork();
        await DimensionTestData.SeedDimensions(db);
        return (db, new AccountingDimensionService(db));
    }

    private static AccountingDimension Changes(string name, bool active, DimensionRole role) =>
        new() { Name = name, Active = active, Role = role };

    [Fact]
    public async Task Update_renames_and_activates()
    {
        var (db, service) = await CreateAsync();

        var result = await service.UpdateAsync(2, Changes("  Veículos  ", true, DimensionRole.Vehicle));

        Assert.NotNull(result);
        var saved = await db.Context.AccountingDimensions.AsNoTracking().SingleAsync(x => x.Number == 2);
        Assert.Equal("Veículos", saved.Name);
        Assert.True(saved.Active);
        Assert.Equal(DimensionRole.Vehicle, saved.Role);
    }

    [Fact]
    public async Task Second_dimension_with_same_role_is_rejected()
    {
        var (_, service) = await CreateAsync();
        await service.UpdateAsync(2, Changes("Dimensão 2", true, DimensionRole.Vehicle));

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            service.UpdateAsync(3, Changes("Dimensão 3", true, DimensionRole.Vehicle)));

        Assert.Equal("A dimensão Dimensão 2 já tem o papel Veículo.", ex.Message);
    }

    [Fact]
    public async Task Role_none_can_repeat()
    {
        var (db, service) = await CreateAsync();

        await service.UpdateAsync(2, Changes("Dimensão 2", true, DimensionRole.None));
        await service.UpdateAsync(3, Changes("Dimensão 3", true, DimensionRole.None));

        Assert.Equal(2, await db.Context.AccountingDimensions.CountAsync(x => x.Active && x.Number > 1));
    }

    [Fact]
    public async Task Dimension_1_cannot_be_deactivated()
    {
        var (_, service) = await CreateAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            service.UpdateAsync(1, Changes("Dimensão 1", false, DimensionRole.None)));

        Assert.Equal("A dimensão 1 é o centro de custo e não pode ser desativada.", ex.Message);
    }

    [Fact]
    public async Task Name_is_required()
    {
        var (_, service) = await CreateAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            service.UpdateAsync(2, Changes("   ", true, DimensionRole.None)));

        Assert.Equal("Informe o nome da dimensão.", ex.Message);
    }
}
