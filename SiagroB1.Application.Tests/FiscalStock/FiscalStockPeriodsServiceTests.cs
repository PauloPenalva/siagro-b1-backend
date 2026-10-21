using Microsoft.EntityFrameworkCore;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Application.Services.FiscalStock;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.FiscalStock;

public class FiscalStockPeriodsServiceTests
{
    private static async Task<(IUnitOfWork Db, FiscalStockPeriodsService Service)> Arrange(bool withOpening = true)
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.Items.Add(new Item { ItemCode = "SOJA", ItemName = "SOJA EM GRAOS" });
        if (withOpening)
            db.Context.FiscalInventoryOpeningBalances.Add(new FiscalInventoryOpeningBalance
            {
                BranchCode = "01", ItemCode = "SOJA", ItemName = "SOJA EM GRAOS", UnitOfMeasureCode = "SC",
                BalanceDate = new DateTime(2026, 9, 1), Quantity = 100m, TotalValue = 5000m,
            });
        await db.SaveChangesAsync();
        return (db, new FiscalStockPeriodsService(db));
    }

    private static void AddAdjustment(IUnitOfWork db, FiscalStockAdjustmentDirection direction, decimal quantity,
        decimal value, DateTime date, string number) =>
        db.Context.FiscalStockAdjustments.Add(new FiscalStockAdjustment
        {
            BranchCode = "01", ItemCode = "SOJA", ItemName = "SOJA EM GRAOS", UnitOfMeasureCode = "SC",
            AdjustmentDate = date, Direction = direction, Quantity = quantity, TotalValue = value,
            Reason = "teste", DocNumber = number,
        });

    [Fact]
    public async Task Close_without_opening_balance_is_refused()
    {
        var (_, service) = await Arrange(withOpening: false);

        var ex = await Assert.ThrowsAsync<DefaultException>(() => service.CloseAsync("01", 2026, 9, "paulo"));

        Assert.Equal("Lance o Saldo Inicial do Estoque antes de fechar.", ex.Message);
    }

    [Fact]
    public async Task Close_with_pending_exit_is_refused_listing_the_item()
    {
        var (db, service) = await Arrange();
        AddAdjustment(db, FiscalStockAdjustmentDirection.Out, 150m, 5000m, new DateTime(2026, 9, 10), "AJ000001");
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => service.CloseAsync("01", 2026, 9, "paulo"));

        Assert.Contains("SOJA SOJA EM GRAOS: saldo -50 / R$ 0,00", ex.Message);
        Assert.False(await db.Context.FiscalStockPeriods.AnyAsync());
    }

    [Fact]
    public async Task Close_with_zero_quantity_and_value_is_refused()
    {
        var (db, service) = await Arrange();
        AddAdjustment(db, FiscalStockAdjustmentDirection.Out, 100m, 4000m, new DateTime(2026, 9, 10), "AJ000001");
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => service.CloseAsync("01", 2026, 9, "paulo"));

        Assert.Contains("SOJA SOJA EM GRAOS: saldo 0 / R$ 1.000,00", ex.Message);
    }

    [Fact]
    public async Task Close_saves_snapshot_equal_to_open_layers_and_event()
    {
        var (db, service) = await Arrange();
        AddAdjustment(db, FiscalStockAdjustmentDirection.In, 20m, 1200m, new DateTime(2026, 9, 15), "AJ000001");
        await db.SaveChangesAsync();

        var period = await service.CloseAsync("01", 2026, 9, "paulo");

        Assert.Equal(FiscalStockPeriodStatus.Closed, period.Status);
        Assert.Equal("paulo", period.ClosedBy);
        var layers = await db.Context.FiscalStockPeriodLayers.Where(l => l.PeriodKey == period.Key)
            .OrderBy(l => l.Sequence).ToListAsync();
        Assert.Equal(2, layers.Count);
        Assert.Equal((100m, 5000m, new DateTime(2026, 9, 1), 1), (layers[0].Quantity, layers[0].Value, layers[0].LayerDate, layers[0].Sequence));
        Assert.Equal((20m, 1200m, new DateTime(2026, 9, 15), 2), (layers[1].Quantity, layers[1].Value, layers[1].LayerDate, layers[1].Sequence));
        Assert.Equal("SOJA EM GRAOS", layers[0].ItemName);
        Assert.Equal("SC", layers[0].UnitOfMeasureCode);
        var ev = Assert.Single(await db.Context.FiscalStockPeriodEvents.ToListAsync());
        Assert.Equal(FiscalStockPeriodEventType.Closed, ev.EventType);
        Assert.Equal("paulo", ev.By);
    }

    [Fact]
    public async Task Close_october_without_september_closed_is_refused()
    {
        var (_, service) = await Arrange();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => service.CloseAsync("01", 2026, 10, "paulo"));

        Assert.Equal("Feche antes 09/2026.", ex.Message);
    }

    [Fact]
    public async Task Close_month_before_first_opening_is_refused()
    {
        var (_, service) = await Arrange();

        await Assert.ThrowsAsync<DefaultException>(() => service.CloseAsync("01", 2026, 8, "paulo"));
    }

    [Fact]
    public async Task Close_october_after_september_is_accepted()
    {
        var (_, service) = await Arrange();
        await service.CloseAsync("01", 2026, 9, "paulo");

        var october = await service.CloseAsync("01", 2026, 10, "paulo");

        Assert.Equal(FiscalStockPeriodStatus.Closed, october.Status);
    }

    [Fact]
    public async Task Reopen_september_with_october_closed_is_refused()
    {
        var (_, service) = await Arrange();
        await service.CloseAsync("01", 2026, 9, "paulo");
        await service.CloseAsync("01", 2026, 10, "paulo");

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            service.ReopenAsync("01", 2026, 9, "Lançamento atrasado", "paulo"));

        Assert.Equal("Reabra antes 10/2026.", ex.Message);
    }

    [Fact]
    public async Task Reopen_without_reason_is_refused()
    {
        var (_, service) = await Arrange();
        await service.CloseAsync("01", 2026, 9, "paulo");

        await Assert.ThrowsAsync<DefaultException>(() => service.ReopenAsync("01", 2026, 9, "  ", "paulo"));
    }

    [Fact]
    public async Task Reopen_deletes_snapshot_and_saves_event()
    {
        var (db, service) = await Arrange();
        await service.CloseAsync("01", 2026, 9, "paulo");

        var period = await service.ReopenAsync("01", 2026, 9, "Lançamento atrasado", "maria");

        Assert.Equal(FiscalStockPeriodStatus.Open, period.Status);
        Assert.Equal("maria", period.ReopenedBy);
        Assert.Equal("Lançamento atrasado", period.ReopenReason);
        Assert.False(await db.Context.FiscalStockPeriodLayers.AnyAsync());
        var events = await db.Context.FiscalStockPeriodEvents.OrderBy(e => e.EventType).ToListAsync();
        Assert.Equal(2, events.Count);
        Assert.Equal(FiscalStockPeriodEventType.Reopened, events[1].EventType);
        Assert.Equal("Lançamento atrasado", events[1].Reason);

        var again = await service.CloseAsync("01", 2026, 9, "paulo");
        Assert.Equal(period.Key, again.Key);
    }

    [Fact]
    public void Edm_exposes_FiscalStockPeriods_and_actions()
    {
        var builder = new ODataConventionModelBuilder();
        builder.ConfigureODataEntities();
        var model = builder.GetEdmModel();

        Assert.NotNull(model.EntityContainer.FindEntitySet("FiscalStockPeriods"));
        Assert.NotEmpty(model.EntityContainer.FindOperationImports("FiscalStockPeriodsClose"));
        Assert.NotEmpty(model.EntityContainer.FindOperationImports("FiscalStockPeriodsReopen"));
    }
}
