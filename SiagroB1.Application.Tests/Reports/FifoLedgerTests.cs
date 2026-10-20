using SiagroB1.Infra.FiscalStock;

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

        Assert.Equal(3, r.Lines.Count);
        var entry = r.Lines[1];
        Assert.Equal(5m, entry.BalanceQuantity);
        Assert.Equal(200.00m, entry.BalanceValue);
        var adj = r.Lines[2];
        Assert.True(adj.IsNegativeBalanceAdjustment);
        Assert.Equal(0m, adj.QuantityOut);
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

        var comp = r.Lines[2];
        Assert.True(comp.PendingValue);
        Assert.Equal(0m, comp.ValueIn);
        Assert.Equal(0m, comp.BalanceValue);
        Assert.Equal(230.00m, r.Lines[3].ValueIn);

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

    [Fact]
    public void Entry_PartiallyCoversDeficit()
    {
        var r = FifoLedger.Run([
            M(InventoryMovementKind.Exit, 5),
            M(InventoryMovementKind.Entry, 3, 60m, D1.AddDays(1))]);

        Assert.Equal(-2m, r.Lines[1].BalanceQuantity);
        Assert.Equal(60.00m, r.Lines[1].BalanceValue);
        var adj = r.Lines[2];
        Assert.True(adj.IsNegativeBalanceAdjustment);
        Assert.Equal(60.00m, adj.ValueOut);
        Assert.Equal(-2m, adj.BalanceQuantity);
        Assert.Equal(0.00m, adj.BalanceValue);
        Assert.Empty(r.OpenLayers);
    }

    [Fact]
    public void PurchaseReturn_BeyondOriginLayer_UsesFifoForExcess()
    {
        var a = M(InventoryMovementKind.Entry, 10, 100m);
        var b = M(InventoryMovementKind.Entry, 10, 200m);
        var r = FifoLedger.Run([a, b,
            M(InventoryMovementKind.PurchaseReturn, 12, date: D1.AddDays(1), origin: b.LineKey)]);

        // 10 de B (200,00) + 2 de A pelo FIFO (20,00)
        Assert.Equal(220.00m, r.Lines.Last().ValueOut);
        var la = Assert.Single(r.OpenLayers);
        Assert.Equal(a.LineKey, la.LineKey);
        Assert.Equal(8m, la.Quantity);
        Assert.Equal(80.00m, la.Value);
    }

    [Fact]
    public void SalesReturn_OfExitSpanningTwoLayers_UsesWeightedCost()
    {
        var s = M(InventoryMovementKind.Exit, 15, date: D1.AddDays(1));
        var r = FifoLedger.Run([
            M(InventoryMovementKind.Entry, 10, 100m), M(InventoryMovementKind.Entry, 10, 200m), s,
            M(InventoryMovementKind.SalesReturn, 3, date: D1.AddDays(2), origin: s.LineKey)]);

        // saída 15 = 100,00 + 100,00 → 13,333333/un; 3 un = 40,00
        Assert.Equal(200.00m, r.Lines[2].ValueOut);
        Assert.Equal(40.00m, r.Lines.Last().ValueIn);
    }

    [Fact]
    public void Returns_WithUnknownOrigin()
    {
        var r = FifoLedger.Run([
            M(InventoryMovementKind.Entry, 10, 100m), M(InventoryMovementKind.Entry, 10, 200m),
            M(InventoryMovementKind.PurchaseReturn, 4, date: D1.AddDays(1), origin: Guid.NewGuid()),
            M(InventoryMovementKind.SalesReturn, 2, date: D1.AddDays(2), origin: Guid.NewGuid())]);

        Assert.Equal(40.00m, r.Lines[2].ValueOut);
        // devolução de venda sem saída conhecida: custo da camada mais antiga em aberto (10,00)
        Assert.Equal(20.00m, r.Lines[3].ValueIn);
        Assert.Equal(10m, r.Lines[3].UnitCost);
        Assert.False(r.Lines[3].CostNotFound);
    }

    [Fact]
    public void SalesReturn_WithUnknownOrigin_UsesOldestOpenLayerCost()
    {
        var r = FifoLedger.Run([
            M(InventoryMovementKind.Entry, 10, 150m), M(InventoryMovementKind.Entry, 10, 300m),
            M(InventoryMovementKind.SalesReturn, 2, date: D1.AddDays(1), origin: null)]);

        Assert.Equal(15m, r.Lines[2].UnitCost);
        Assert.Equal(30.00m, r.Lines[2].ValueIn);
        Assert.False(r.Lines[2].CostNotFound);
    }

    [Fact]
    public void SalesReturn_WithUnknownOriginAndNoLayer_FlagsCostNotFound()
    {
        var r = FifoLedger.Run([M(InventoryMovementKind.SalesReturn, 2, origin: Guid.NewGuid())]);

        Assert.Equal(0m, r.Lines[0].ValueIn);
        Assert.True(r.Lines[0].CostNotFound);
        Assert.Equal(2m, r.Lines[0].BalanceQuantity);
    }

    [Fact]
    public void Invariants_HoldOverMixedScenario()
    {
        var a = M(InventoryMovementKind.Entry, 10, 100m);
        var s = M(InventoryMovementKind.Exit, 7, date: D1.AddDays(1));
        var r = FifoLedger.Run([
            M(InventoryMovementKind.Opening, 3, 31m), a,
            M(InventoryMovementKind.Entry, 7, 99.99m),
            s,
            M(InventoryMovementKind.ValueComplement, 0, 12.5m, D1.AddDays(1), a.LineKey),
            M(InventoryMovementKind.PurchaseReturn, 5, date: D1.AddDays(2), origin: a.LineKey),
            M(InventoryMovementKind.Exit, 20, date: D1.AddDays(3)),
            M(InventoryMovementKind.ValueComplement, 0, 7m, D1.AddDays(4), a.LineKey),
            M(InventoryMovementKind.SalesReturn, 2, date: D1.AddDays(4), origin: s.LineKey),
            M(InventoryMovementKind.Entry, 1, 13m, D1.AddDays(5)),
            M(InventoryMovementKind.Entry, 9, 101m, D1.AddDays(6)),
            M(InventoryMovementKind.Exit, 3, date: D1.AddDays(7))]);

        decimal q = 0m, v = 0m, deficit = 0m;
        foreach (var l in r.Lines)
        {
            q += l.QuantityIn - l.QuantityOut;
            v += l.ValueIn - l.ValueOut;
            Assert.Equal(q, l.BalanceQuantity);
            Assert.Equal(v, l.BalanceValue);
            deficit = Math.Max(0m, -l.BalanceQuantity);
        }
        Assert.Contains(r.Lines, l => l.IsNegativeBalanceAdjustment);
        Assert.Contains(r.Lines, l => l.PendingValue);
        Assert.Equal(r.Lines.Last().BalanceValue, r.OpenLayers.Sum(x => x.Value));
        Assert.Equal(r.Lines.Last().BalanceQuantity, r.OpenLayers.Sum(x => x.Quantity) - deficit);
        Assert.All(r.OpenLayers, x => Assert.True(x.Quantity > 0));
    }

    public static TheoryData<string> Scenarios => new() { "simple", "deficit", "deficitWithPending", "partialCover", "mixed" };

    private static List<InventoryMovement> Scenario(string name)
    {
        var a = M(InventoryMovementKind.Entry, 10, 100m);
        return name switch
        {
            "simple" => [a, M(InventoryMovementKind.Exit, 4, date: D1.AddDays(1))],
            "deficit" => [M(InventoryMovementKind.Exit, 10)],
            "deficitWithPending" => [M(InventoryMovementKind.Exit, 10),
                M(InventoryMovementKind.ValueComplement, 0, 25m, D1.AddDays(1))],
            "partialCover" => [M(InventoryMovementKind.Exit, 10),
                M(InventoryMovementKind.Entry, 4, 40m, D1.AddDays(1))],
            _ => [M(InventoryMovementKind.Opening, 3, 31m), a,
                M(InventoryMovementKind.Exit, 20, date: D1.AddDays(1)),
                M(InventoryMovementKind.ValueComplement, 0, 7m, D1.AddDays(2)),
                M(InventoryMovementKind.Entry, 9, 101m, D1.AddDays(3)),
                M(InventoryMovementKind.SalesReturn, 1, date: D1.AddDays(4))],
        };
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void Result_ExposesDeficitAndPendingValue_MatchingLastBalance(string name)
    {
        var r = FifoLedger.Run(Scenario(name));
        var last = r.Lines.Last();

        Assert.Equal(last.BalanceQuantity, r.OpenLayers.Sum(x => x.Quantity) - r.Deficit);
        Assert.Equal(last.BalanceValue, r.OpenLayers.Sum(x => x.Value));
        Assert.True(r.Deficit >= 0m);
    }

    [Fact]
    public void Result_FullDeficitWithPendingComplement()
    {
        var r = FifoLedger.Run(Scenario("deficitWithPending"));

        Assert.Empty(r.OpenLayers);
        Assert.Equal(10m, r.Deficit);
        Assert.Equal(25m, r.PendingValue);
        Assert.Equal(-10m, r.Lines.Last().BalanceQuantity);
        Assert.Equal(0m, r.Lines.Last().BalanceValue);
    }
}
