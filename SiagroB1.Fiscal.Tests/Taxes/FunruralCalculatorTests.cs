using SiagroB1.Fiscal.Taxes;

namespace SiagroB1.Fiscal.Tests.Taxes;

/// <summary>Funrural da linha (spec 2026-10-09 §3.2): cada parcela arredondada a 2 casas sobre o total geral.</summary>
public class FunruralCalculatorTests
{
    [Fact]
    public void Individual_producer_withholds_inss_rat_and_senar()
    {
        var r = FunruralCalculator.Calculate(1500m, 1.32m, 0.11m, 0.20m);

        Assert.Equal((1500m, 1.32m, 0.11m, 0.20m), (r.Base, r.InssRate, r.RatRate, r.SenarRate));
        Assert.Equal((19.80m, 1.65m, 3.00m), (r.InssValue, r.RatValue, r.SenarValue));
        Assert.Equal(24.45m, r.Total);
    }

    [Fact]
    public void Company_producer_has_inss_and_rat_combined()
    {
        var r = FunruralCalculator.Calculate(1500m, 1.98m, 0m, 0.25m);

        Assert.Equal((29.70m, 0m, 3.75m), (r.InssValue, r.RatValue, r.SenarValue));
        Assert.Equal(33.45m, r.Total);
    }

    [Fact]
    public void Court_decision_withholds_only_senar()
    {
        var r = FunruralCalculator.Calculate(1500m, null, null, 0.20m);

        Assert.Equal((1500m, 0m, 0m, 0.20m), (r.Base, r.InssRate, r.RatRate, r.SenarRate));
        Assert.Equal((0m, 0m, 3.00m), (r.InssValue, r.RatValue, r.SenarValue));
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData(0.0, 0.0, 0.0)]
    public void Without_rates_the_line_has_no_funrural(double? inss, double? rat, double? senar)
    {
        var r = FunruralCalculator.Calculate(1500m, (decimal?)inss, (decimal?)rat, (decimal?)senar);

        Assert.Equal(FunruralResult.None, r);
        Assert.Equal(0m, r.Base);
    }

    [Fact]
    public void Half_cent_rounds_away_from_zero()
    {
        // 62,50 × 0,20% = 0,125 → 0,13 (ToEven daria 0,12)
        Assert.Equal(0.13m, FunruralCalculator.Calculate(62.5m, null, null, 0.20m).SenarValue);
    }
}
