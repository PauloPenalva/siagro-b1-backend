using SiagroB1.Reports.Services.Ecf;
using Xunit;

namespace SiagroB1.Application.Tests.Ecf;

public class IncomeTaxCalculatorTests
{
    [Fact]
    public void ComputeIrpj_AnnualProfit_AppliesRateAndSurtaxOver240k()
    {
        var r = IncomeTaxCalculator.ComputeIrpj(1_000_000m, 0m, 0m, 0m, 12);

        Assert.Equal(1_000_000m, r.AdjustedProfit);
        Assert.Equal(1_000_000m, r.TaxableBase);
        Assert.Equal(150_000m, r.Tax);
        Assert.Equal(76_000m, r.Surtax);
        Assert.Equal(226_000m, r.Total);
    }

    [Fact]
    public void ComputeIrpj_Quarterly_SurtaxOver60k()
    {
        var r = IncomeTaxCalculator.ComputeIrpj(100_000m, 0m, 0m, 0m, 3);

        Assert.Equal(15_000m, r.Tax);
        Assert.Equal(4_000m, r.Surtax);
        Assert.Equal(19_000m, r.Total);
    }

    [Fact]
    public void ComputeIrpj_BelowSurtaxThreshold_SurtaxIsZero()
    {
        var r = IncomeTaxCalculatorTestsHelper.Irpj(50_000m, 3);

        Assert.Equal(7_500m, r.Tax);
        Assert.Equal(0m, r.Surtax);
    }

    [Fact]
    public void ComputeCsll_AnnualProfit_Applies9Percent()
    {
        var r = IncomeTaxCalculator.ComputeCsll(1_000_000m, 0m, 0m, 0m);

        Assert.Equal(90_000m, r.Tax);
        Assert.Equal(0m, r.Surtax);
        Assert.Equal(90_000m, r.Total);
    }

    [Fact]
    public void Compute_Loss_AllZeros()
    {
        var irpj = IncomeTaxCalculator.ComputeIrpj(-50_000m, 0m, 0m, 0m, 12);
        var csll = IncomeTaxCalculator.ComputeCsll(-50_000m, 0m, 0m, 0m);

        foreach (var r in new[] { irpj, csll })
        {
            Assert.Equal(-50_000m, r.AdjustedProfit);
            Assert.Equal(0m, r.OffsetLimit);
            Assert.Equal(0m, r.OffsetApplied);
            Assert.Equal(0m, r.TaxableBase);
            Assert.Equal(0m, r.Tax);
            Assert.Equal(0m, r.Surtax);
            Assert.Equal(0m, r.Total);
        }
    }

    [Fact]
    public void Compute_AdditionsAndExclusions_AdjustProfit()
    {
        var r = IncomeTaxCalculator.ComputeIrpj(100_000m, 30_000m, 10_000m, 0m, 12);

        Assert.Equal(120_000m, r.AdjustedProfit);
        Assert.Equal(18_000m, r.Tax);
    }

    [Fact]
    public void Compute_OffsetAboveLimit_IsCappedAt30Percent()
    {
        var r = IncomeTaxCalculator.ComputeIrpj(1_000_000m, 0m, 0m, 500_000m, 12);

        Assert.Equal(300_000m, r.OffsetLimit);
        Assert.Equal(300_000m, r.OffsetApplied);
        Assert.True(r.OffsetExceedsLimit);
        Assert.Equal(700_000m, r.TaxableBase);
        Assert.Equal(105_000m, r.Tax);
        Assert.Equal(46_000m, r.Surtax);
    }

    [Fact]
    public void Compute_OffsetWithinLimit_IsFullyApplied()
    {
        var r = IncomeTaxCalculator.ComputeCsll(1_000_000m, 0m, 0m, 100_000m);

        Assert.Equal(100_000m, r.OffsetApplied);
        Assert.False(r.OffsetExceedsLimit);
        Assert.Equal(900_000m, r.TaxableBase);
        Assert.Equal(81_000m, r.Tax);
    }

    [Fact]
    public void Compute_Rounding_MidpointAwayFromZero()
    {
        // 333,10 x 15% = 49,965 -> 49,97 (nao 49,96 do arredondamento bancario)
        Assert.Equal(49.97m, IncomeTaxCalculator.ComputeIrpj(333.10m, 0m, 0m, 0m, 12).Tax);
        // 333,35 x 9% = 30,0015 -> 30,00 ; 333,50 x 9% = 30,015 -> 30,02
        Assert.Equal(30.02m, IncomeTaxCalculator.ComputeCsll(333.50m, 0m, 0m, 0m).Tax);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public void ComputeIrpj_InvalidMonths_Throws(int months)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => IncomeTaxCalculator.ComputeIrpj(1m, 0m, 0m, 0m, months));
    }

    [Fact]
    public void Compute_NegativeInputs_Throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => IncomeTaxCalculator.ComputeIrpj(1m, -1m, 0m, 0m, 12));
        Assert.Throws<ArgumentOutOfRangeException>(() => IncomeTaxCalculator.ComputeIrpj(1m, 0m, -1m, 0m, 12));
        Assert.Throws<ArgumentOutOfRangeException>(() => IncomeTaxCalculator.ComputeCsll(1m, 0m, 0m, -1m));
    }
}

internal static class IncomeTaxCalculatorTestsHelper
{
    public static IncomeTaxResult Irpj(decimal profit, int months) =>
        IncomeTaxCalculator.ComputeIrpj(profit, 0m, 0m, 0m, months);
}
