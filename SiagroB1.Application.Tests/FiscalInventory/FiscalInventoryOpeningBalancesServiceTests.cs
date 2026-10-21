using SiagroB1.Application.Services.FiscalInventory;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.FiscalInventory;

public class FiscalInventoryOpeningBalancesServiceTests
{
    private static async Task<(IUnitOfWork Db, FiscalInventoryOpeningBalancesService Service)> Arrange(
        string? commercialUom = "SC")
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.Items.Add(new Item { ItemCode = "SOJA", ItemName = "SOJA EM GRAOS" });
        db.Context.Items.Add(new Item { ItemCode = "MILHO", ItemName = "MILHO EM GRAOS" });
        db.Context.Set<ItemComplement>().Add(new ItemComplement { ItemCode = "SOJA", CommercialUnitOfMeasureCode = commercialUom });
        await db.SaveChangesAsync();
        return (db, new FiscalInventoryOpeningBalancesService(db));
    }

    private static FiscalInventoryOpeningBalance Balance(
        string branch = "01", string item = "SOJA", decimal quantity = 100m, decimal total = 5000m, string? unit = null) => new()
    {
        BranchCode = branch, ItemCode = item, BalanceDate = new DateTime(2026, 1, 1), Quantity = quantity, TotalValue = total, UnitOfMeasureCode = unit,
    };

    [Fact]
    public async Task Create_fills_item_name_and_uom_from_item_master()
    {
        var (_, service) = await Arrange();

        var created = await service.CreateAsync(Balance());

        Assert.Equal("SOJA EM GRAOS", created.ItemName);
        Assert.Equal("SC", created.UnitOfMeasureCode);
        Assert.NotEqual(Guid.Empty, created.Key);
    }

    [Fact]
    public async Task Create_keeps_client_unit()
    {
        var (_, service) = await Arrange();

        var created = await service.CreateAsync(Balance(unit: " ton "));

        Assert.Equal("TON", created.UnitOfMeasureCode);
    }

    [Fact]
    public async Task Create_defaults_to_commercial_unit()
    {
        var (_, service) = await Arrange();

        var created = await service.CreateAsync(Balance());

        Assert.Equal("SC", created.UnitOfMeasureCode);
    }

    [Fact]
    public async Task Create_without_any_unit_is_refused()
    {
        var (_, service) = await Arrange(commercialUom: null);

        var ex = await Assert.ThrowsAsync<DefaultException>(() => service.CreateAsync(Balance()));

        Assert.Equal("Informe a unidade de medida.", ex.Message);
    }

    [Fact]
    public async Task Unit_longer_than_column_is_refused()
    {
        var (_, service) = await Arrange();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => service.CreateAsync(Balance(unit: "TONEL")));

        Assert.Equal("Unidade de medida inválida.", ex.Message);
    }

    [Fact]
    public async Task Update_keeps_client_unit()
    {
        var (_, service) = await Arrange();
        var created = await service.CreateAsync(Balance());

        created.UnitOfMeasureCode = "kg";
        var updated = await service.UpdateAsync(created.Key, created);

        Assert.Equal("KG", updated!.UnitOfMeasureCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Quantity_must_be_greater_than_zero(int quantity)
    {
        var (_, service) = await Arrange();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => service.CreateAsync(Balance(quantity: quantity)));

        Assert.Equal("Informe uma quantidade maior que zero.", ex.Message);
    }

    [Fact]
    public async Task Negative_total_value_is_refused_but_zero_is_accepted()
    {
        var (_, service) = await Arrange();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => service.CreateAsync(Balance(total: -0.01m)));
        Assert.Equal("O valor não pode ser negativo.", ex.Message);

        var created = await service.CreateAsync(Balance(total: 0m));
        Assert.Equal(0m, created.TotalValue);
    }

    [Theory]
    [InlineData("", "SOJA", "Informe a filial.")]
    [InlineData("01", "", "Informe o produto.")]
    public async Task Branch_and_item_are_required(string branch, string item, string message)
    {
        var (_, service) = await Arrange();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => service.CreateAsync(Balance(branch, item)));

        Assert.Equal(message, ex.Message);
    }

    [Fact]
    public async Task Unknown_item_is_refused()
    {
        var (_, service) = await Arrange();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => service.CreateAsync(Balance(item: "XYZ")));

        Assert.Equal("Produto não encontrado.", ex.Message);
    }

    [Fact]
    public async Task Same_branch_and_item_is_refused_but_other_branch_or_item_is_accepted()
    {
        var (_, service) = await Arrange();
        await service.CreateAsync(Balance());

        var ex = await Assert.ThrowsAsync<DefaultException>(() => service.CreateAsync(Balance()));
        Assert.Equal("Já existe saldo inicial para este produto nesta filial.", ex.Message);

        await service.CreateAsync(Balance(branch: "02"));
        await service.CreateAsync(Balance(item: "MILHO", unit: "SC"));
    }

    [Fact]
    public async Task Update_changes_values_and_revalidates_uniqueness_ignoring_itself()
    {
        var (_, service) = await Arrange();
        var first = await service.CreateAsync(Balance());
        await service.CreateAsync(Balance(branch: "02"));

        first.Quantity = 250m;
        var updated = await service.UpdateAsync(first.Key, first);
        Assert.Equal(250m, updated!.Quantity);

        first.BranchCode = "02";
        var ex = await Assert.ThrowsAsync<DefaultException>(() => service.UpdateAsync(first.Key, first));
        Assert.Equal("Já existe saldo inicial para este produto nesta filial.", ex.Message);
    }

    [Fact]
    public async Task Update_of_unknown_key_returns_null_and_delete_works()
    {
        var (_, service) = await Arrange();
        Assert.Null(await service.UpdateAsync(Guid.NewGuid(), Balance()));

        var created = await service.CreateAsync(Balance());
        Assert.True(await service.DeleteAsync(created.Key));
        Assert.Null(await service.GetByKeyAsync(created.Key));
        Assert.False(await service.DeleteAsync(created.Key));
    }

    [Fact]
    public async Task Update_patching_tracked_entity_out_of_closed_month_is_refused()
    {
        var (db, service) = await Arrange();
        var created = await service.CreateAsync(Balance());
        db.Context.FiscalStockPeriods.Add(new FiscalStockPeriod
            { BranchCode = "01", Year = 2026, Month = 1, Status = FiscalStockPeriodStatus.Closed });
        await db.SaveChangesAsync();

        var tracked = (await service.GetByKeyAsync(created.Key))!;
        tracked.BalanceDate = new DateTime(2026, 3, 1);
        var ex = await Assert.ThrowsAsync<DefaultException>(() => service.UpdateAsync(created.Key, tracked));

        Assert.Equal(
            "O estoque fiscal de 01/2026 da filial 01 está fechado. Use uma data de entrada no estoque a partir de 01/02/2026 ou peça a reabertura do mês.",
            ex.Message);
    }
}
