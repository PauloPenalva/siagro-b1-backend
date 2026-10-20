using SiagroB1.Reports.Services.Fiscal;

namespace SiagroB1.Application.Tests.Reports;

public class FifoLedgerTests
{
    private static readonly DateTime D1 = new(2026, 1, 1);
    private static int _seq;

    private static InventoryMovement M(InventoryMovementKind kind, decimal qty, decimal value = 0m,
        DateTime? date = null, Guid? origin = null, Guid? key = null) =>
        new(key ?? Guid.NewGuid(), date ?? D1, ++_seq, kind, qty, value, origin, "KG", null);

    [Fact]
    public void Exit_ConsumesOldestLayerFirst()
    {
        var b = M(InventoryMovementKind.Entry, 10, 1200m, D1.AddDays(1));
        var r = FifoLedger.Run([
            M(InventoryMovementKind.Entry, 10, 1000m), b,
            M(InventoryMovementKind.Exit, 15, date: D1.AddDays(2))]);

        var exit = r.Lines.Last();
        Assert.Equal(1600.00m, exit.ValueOut);
        Assert.Equal(5m, exit.BalanceQuantity);
        Assert.Equal(600.00m, exit.BalanceValue);
        var layer = Assert.Single(r.OpenLayers);
        Assert.Equal(b.LineKey, layer.LineKey);
        Assert.Equal(5m, layer.Quantity);
        Assert.Equal(600.00m, layer.Value);
    }

    [Fact]
    public void Exit_SplitsLastCentInLastConsumption()
    {
        var r = FifoLedger.Run([
            M(InventoryMovementKind.Entry, 3, 100m),
            M(InventoryMovementKind.Exit, 1), M(InventoryMovementKind.Exit, 1), M(InventoryMovementKind.Exit, 1)]);

        Assert.Equal([33.33m, 33.33m, 33.34m], r.Lines.Skip(1).Select(l => l.ValueOut));
        Assert.Equal(0m, r.Lines.Last().BalanceQuantity);
        Assert.Equal(0.00m, r.Lines.Last().BalanceValue);
        Assert.Empty(r.OpenLayers);
    }

    [Fact]
    public void Exit_WithoutBalance_GoesOutAtZeroAndFlags()
    {
        var r = FifoLedger.Run([M(InventoryMovementKind.Exit, 5)]);
        var l = Assert.Single(r.Lines);
        Assert.Equal(0m, l.ValueOut);
        Assert.True(l.InsufficientBalance);
        Assert.Equal(-5m, l.BalanceQuantity);
        Assert.Equal(0m, l.BalanceValue);
    }

    [Fact]
    public void Entry_AfterDeficit_CreatesAdjustment()
    {
        var r = FifoLedger.Run([
            M(InventoryMovementKind.Exit, 5),
            M(InventoryMovementKind.Entry, 10, 200m, D1.AddDays(1))]);

        var adj = Assert.Single(r.Lines, l => l.IsNegativeBalanceAdjustment);
        Assert.Equal(5m, adj.QuantityOut);
        Assert.Equal(100.00m, adj.ValueOut);
        var last = r.Lines.Last();
        Assert.Equal(5m, last.BalanceQuantity);
        Assert.Equal(100.00m, last.BalanceValue);
        Assert.Equal(last.BalanceQuantity, r.OpenLayers.Sum(x => x.Quantity));
        Assert.Equal(last.BalanceValue, r.OpenLayers.Sum(x => x.Value));
    }

    [Fact]
    public void PurchaseReturn_UsesOriginLayerCost()
    {
        var a = M(InventoryMovementKind.Entry, 10, 100m);
        var b = M(InventoryMovementKind.Entry, 10, 200m);
        var r = FifoLedger.Run([a, b,
            M(InventoryMovementKind.PurchaseReturn, 4, date: D1.AddDays(1), origin: b.LineKey)]);

        Assert.Equal(80.00m, r.Lines.Last().ValueOut);
        var lb = Assert.Single(r.OpenLayers, x => x.LineKey == b.LineKey);
        Assert.Equal(6m, lb.Quantity);
        Assert.Equal(120.00m, lb.Value);
        var la = Assert.Single(r.OpenLayers, x => x.LineKey == a.LineKey);
        Assert.Equal(10m, la.Quantity);
        Assert.Equal(100.00m, la.Value);
    }

    [Fact]
    public void SalesReturn_UsesOriginExitCost()
    {
        var s = M(InventoryMovementKind.Exit, 4, date: D1.AddDays(1));
        var r = FifoLedger.Run([M(InventoryMovementKind.Entry, 10, 100m), s,
            M(InventoryMovementKind.SalesReturn, 2, date: D1.AddDays(2), origin: s.LineKey)]);

        Assert.Equal(40.00m, r.Lines[1].ValueOut);
        Assert.Equal(20.00m, r.Lines.Last().ValueIn);
    }

    [Fact]
    public void ValueComplement_AddsToOriginLayer()
    {
        var a = M(InventoryMovementKind.Entry, 10, 100m);
        var r = FifoLedger.Run([a,
            M(InventoryMovementKind.ValueComplement, 0, 50m, D1.AddDays(1), a.LineKey)]);

        var la = Assert.Single(r.OpenLayers);
        Assert.Equal(10m, la.Quantity);
        Assert.Equal(150.00m, la.Value);
        Assert.Equal(150.00m, r.Lines.Last().BalanceValue);
    }

    [Fact]
    public void ValueComplement_WithNoOpenLayer_GoesToNextLayer()
    {
        var a = M(InventoryMovementKind.Entry, 10, 100m);
        var b = M(InventoryMovementKind.Entry, 10, 200m, D1.AddDays(3));
        var r = FifoLedger.Run([a,
            M(InventoryMovementKind.Exit, 10, date: D1.AddDays(1)),
            M(InventoryMovementKind.ValueComplement, 0, 30m, D1.AddDays(2), a.LineKey), b]);

        var lb = Assert.Single(r.OpenLayers);
        Assert.Equal(b.LineKey, lb.LineKey);
        Assert.Equal(10m, lb.Quantity);
        Assert.Equal(230.00m, lb.Value);
        Assert.Equal(230.00m, r.Lines.Last().BalanceValue);
    }

    [Fact]
    public void Opening_IsOldestLayer()
    {
        var r = FifoLedger.Run([
            M(InventoryMovementKind.Entry, 5, 100m),
            M(InventoryMovementKind.Opening, 5, 50m),
            M(InventoryMovementKind.Exit, 5, date: D1.AddDays(1))]);

        Assert.Equal(50.00m, r.Lines.Last().ValueOut);
    }

    [Fact]
    public void SameDay_EntriesBeforeExits()
    {
        var r = FifoLedger.Run([
            M(InventoryMovementKind.Exit, 5),
            M(InventoryMovementKind.Entry, 10, 100m)]);

        Assert.DoesNotContain(r.Lines, l => l.InsufficientBalance || l.IsNegativeBalanceAdjustment);
        Assert.Equal(50.00m, r.Lines.Last().ValueOut);
    }
}
