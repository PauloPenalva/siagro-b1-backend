using SiagroB1.Application.Services.PurchaseDischarges;

namespace SiagroB1.Application.Tests.PurchaseDischarges;

/// <summary>
/// Spec §2: a diferença do item (líquido − quantidade) é rateada pela participação de cada Expedição
/// no peso da CARGA inteira (CIF ou não); só as CIF viram linha. O resto do arredondamento vai para a
/// maior participação CIF, para a soma das linhas bater com a parte CIF da diferença.
/// </summary>
public class PurchaseDischargeAllocationPlannerTests
{
    private static DischargeShipment Ship(decimal weight, bool cif) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), weight, cif);

    [Fact]
    public void Single_cif_expedition_takes_the_whole_difference()
    {
        var lines = PurchaseDischargeAllocationPlanner.Plan(1000m, 970m, [Ship(1000m, true)]);

        var line = Assert.Single(lines);
        Assert.Equal(-30m, line.AdjustmentVolume);
        Assert.Equal(1000m, line.OriginVolume);
    }

    [Fact]
    public void Gain_is_positive()
    {
        var lines = PurchaseDischargeAllocationPlanner.Plan(1000m, 1012m, [Ship(1000m, true)]);

        Assert.Equal(12m, Assert.Single(lines).AdjustmentVolume);
    }

    [Fact]
    public void Fob_expeditions_dilute_but_get_no_line()
    {
        var cif = Ship(600m, true);
        var lines = PurchaseDischargeAllocationPlanner.Plan(1000m, 900m, [cif, Ship(400m, false)]);

        var line = Assert.Single(lines);
        Assert.Same(cif, line.Shipment);
        Assert.Equal(-60m, line.AdjustmentVolume);
        Assert.Equal(600m, line.OriginVolume);
    }

    [Fact]
    public void Rounding_remainder_goes_to_the_largest_cif_share()
    {
        var a = Ship(1m, true);
        var b = Ship(1m, true);
        var c = Ship(1m, true);

        var lines = PurchaseDischargeAllocationPlanner.Plan(100m, 99.999m, [a, b, c]);

        Assert.Single(lines);
        Assert.Equal(-0.001m, lines.Sum(l => l.AdjustmentVolume));
    }

    [Fact]
    public void Rounding_keeps_the_origin_total_when_all_lines_survive()
    {
        var a = Ship(1m, true);
        var b = Ship(1m, true);
        var c = Ship(1m, true);

        var lines = PurchaseDischargeAllocationPlanner.Plan(100m, 99.997m, [a, b, c]);

        Assert.Equal(3, lines.Count);
        Assert.Equal(-0.003m, lines.Sum(l => l.AdjustmentVolume));
        Assert.Equal(100m, lines.Sum(l => l.OriginVolume));
    }

    [Theory]
    [InlineData(1000, 1000)]
    public void No_difference_no_lines(decimal quantity, decimal net)
    {
        Assert.Empty(PurchaseDischargeAllocationPlanner.Plan(quantity, net, [Ship(1000m, true)]));
    }

    [Fact]
    public void No_cif_expedition_no_lines()
    {
        Assert.Empty(PurchaseDischargeAllocationPlanner.Plan(1000m, 970m, [Ship(1000m, false)]));
    }

    [Fact]
    public void Empty_load_no_lines()
    {
        Assert.Empty(PurchaseDischargeAllocationPlanner.Plan(1000m, 970m, []));
    }
}
