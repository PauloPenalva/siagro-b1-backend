using SiagroB1.Application.Services.FiscalStock;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Web.ODataConfig;
using Microsoft.OData.ModelBuilder;

namespace SiagroB1.Application.Tests.FiscalStock;

public class FiscalStockAdjustmentsServiceTests
{
    private static async Task<(IUnitOfWork Db, FiscalStockAdjustmentsService Service)> Arrange(bool withStock = false)
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.Items.Add(new Item { ItemCode = "SOJA", ItemName = "SOJA EM GRAOS" });
        db.Context.Set<ItemComplement>().Add(new ItemComplement { ItemCode = "SOJA", CommercialUnitOfMeasureCode = "SC" });
        if (withStock)
            db.Context.FiscalInventoryOpeningBalances.Add(new FiscalInventoryOpeningBalance
            {
                BranchCode = "01", ItemCode = "SOJA", ItemName = "SOJA EM GRAOS", UnitOfMeasureCode = "SC",
                BalanceDate = new DateTime(2026, 1, 1), Quantity = 100m, TotalValue = 5000m,
            });
        await db.SaveChangesAsync();
        return (db, new FiscalStockAdjustmentsService(db));
    }

    private static FiscalStockAdjustment Adjustment(
        FiscalStockAdjustmentDirection direction = FiscalStockAdjustmentDirection.In,
        decimal quantity = 10m, decimal total = 500m, string reason = "Sobra de inventário", DateTime? date = null,
        string branch = "01") => new()
    {
        BranchCode = branch, ItemCode = "SOJA", AdjustmentDate = date ?? new DateTime(2026, 2, 10),
        Direction = direction, Quantity = quantity, TotalValue = total, Reason = reason,
    };

    [Fact]
    public async Task Create_numbers_adjustments_in_sequence_per_branch_and_fills_item()
    {
        var (_, service) = await Arrange();

        var first = await service.CreateAsync(Adjustment(), "paulo");
        var second = await service.CreateAsync(Adjustment(), "paulo");
        var otherBranch = await service.CreateAsync(Adjustment(branch: "02"), "paulo");

        Assert.Equal("AJ000001", first.DocNumber);
        Assert.Equal("AJ000002", second.DocNumber);
        Assert.Equal("AJ000001", otherBranch.DocNumber);
        Assert.Equal("SOJA EM GRAOS", first.ItemName);
        Assert.Equal("SC", first.UnitOfMeasureCode);
        Assert.Equal("paulo", first.CreatedBy);
    }

    [Fact]
    public async Task Create_without_reason_is_refused()
    {
        var (_, service) = await Arrange();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => service.CreateAsync(Adjustment(reason: "  "), "paulo"));

        Assert.Equal("Informe o motivo do ajuste.", ex.Message);
    }

    [Fact]
    public async Task Create_with_zero_quantity_is_refused()
    {
        var (_, service) = await Arrange();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => service.CreateAsync(Adjustment(quantity: 0m), "paulo"));

        Assert.Equal("Informe uma quantidade maior que zero.", ex.Message);
    }

    [Fact]
    public async Task Out_above_stock_quantity_is_refused()
    {
        var (_, service) = await Arrange(withStock: true);

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            service.CreateAsync(Adjustment(FiscalStockAdjustmentDirection.Out, quantity: 100.5m, total: 100m), "paulo"));

        Assert.Equal("Saldo insuficiente para o ajuste de saída: disponível 100.", ex.Message);
    }

    [Fact]
    public async Task Out_above_stock_value_is_refused()
    {
        var (_, service) = await Arrange(withStock: true);

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            service.CreateAsync(Adjustment(FiscalStockAdjustmentDirection.Out, quantity: 10m, total: 5000.01m), "paulo"));

        Assert.Equal("O valor do ajuste passa do valor em estoque: disponível R$ 5.000,00.", ex.Message);
    }

    [Fact]
    public async Task Out_within_stock_is_accepted()
    {
        var (_, service) = await Arrange(withStock: true);

        var created = await service.CreateAsync(Adjustment(FiscalStockAdjustmentDirection.Out, quantity: 100m, total: 5000m), "paulo");

        Assert.Equal("AJ000001", created.DocNumber);
    }

    [Fact]
    public async Task Editing_an_out_does_not_count_itself_against_the_stock()
    {
        var (_, service) = await Arrange(withStock: true);
        var created = await service.CreateAsync(Adjustment(FiscalStockAdjustmentDirection.Out, quantity: 80m, total: 4000m), "paulo");

        created.Quantity = 100m;
        created.TotalValue = 5000m;
        var updated = await service.UpdateAsync(created.Key, created);

        Assert.Equal(100m, updated!.Quantity);
    }

    [Fact]
    public async Task Date_in_closed_month_is_refused_on_create_update_and_delete()
    {
        var (db, service) = await Arrange();
        var created = await service.CreateAsync(Adjustment(date: new DateTime(2026, 3, 5)), "paulo");
        db.Context.FiscalStockPeriods.Add(new FiscalStockPeriod
            { BranchCode = "01", Year = 2026, Month = 2, Status = FiscalStockPeriodStatus.Closed });
        db.Context.FiscalStockPeriods.Add(new FiscalStockPeriod
            { BranchCode = "01", Year = 2026, Month = 3, Status = FiscalStockPeriodStatus.Closed });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<DefaultException>(() => service.CreateAsync(Adjustment(date: new DateTime(2026, 2, 10)), "paulo"));
        await Assert.ThrowsAsync<DefaultException>(() => service.DeleteAsync(created.Key));

        created.AdjustmentDate = new DateTime(2026, 4, 5);
        await Assert.ThrowsAsync<DefaultException>(() => service.UpdateAsync(created.Key, created));
    }

    [Fact]
    public void Edm_exposes_FiscalStockAdjustments()
    {
        var builder = new ODataConventionModelBuilder();
        builder.ConfigureODataEntities();

        Assert.NotNull(builder.GetEdmModel().EntityContainer.FindEntitySet("FiscalStockAdjustments"));
    }
}
