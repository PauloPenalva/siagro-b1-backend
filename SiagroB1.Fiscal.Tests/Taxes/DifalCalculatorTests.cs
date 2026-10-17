using SiagroB1.Domain.Enums;
using SiagroB1.Fiscal.Taxes;

namespace SiagroB1.Fiscal.Tests.Taxes;

public class DifalCalculatorTests
{
    [Fact]
    public void Single_base()
    {
        var r = DifalCalculator.Calculate(new DifalInput(1000m, 12m, 120m, 18m, 0m, DifalBaseRule.Single));

        Assert.Equal(1000m, r.Base);
        Assert.Equal(60m, r.Value);
        Assert.Equal(0m, r.FcpValue);
        Assert.Equal(18m, r.InternalRate);
        Assert.Equal(12m, r.InterstateRate);
    }

    [Fact]
    public void Double_base()
    {
        var r = DifalCalculator.Calculate(new DifalInput(1000m, 12m, 120m, 18m, 0m, DifalBaseRule.Double));

        Assert.Equal(1073.17m, r.Base);
        Assert.Equal(73.17m, r.Value);
        Assert.Equal(0m, r.FcpValue);
    }

    [Fact]
    public void Double_base_with_fcp()
    {
        var r = DifalCalculator.Calculate(new DifalInput(1000m, 12m, 120m, 18m, 2m, DifalBaseRule.Double));

        Assert.Equal(1100m, r.Base);
        Assert.Equal(78m, r.Value);
        Assert.Equal(2m, r.FcpRate);
        Assert.Equal(22m, r.FcpValue);
    }

    [Fact]
    public void Origin_icms_missing_uses_interstate_rate()
    {
        var withOrigin = DifalCalculator.Calculate(new DifalInput(1000m, 12m, 120m, 18m, 0m, DifalBaseRule.Double));
        var missing = DifalCalculator.Calculate(new DifalInput(1000m, 12m, 0m, 18m, 0m, DifalBaseRule.Double));

        Assert.Equal(withOrigin, missing);
    }

    [Fact]
    public void Interstate_not_below_internal_gives_zero()
    {
        var r = DifalCalculator.Calculate(new DifalInput(1000m, 18m, 180m, 18m, 0m, DifalBaseRule.Single));

        Assert.Equal(0m, r.Value);
    }

    [Fact]
    public void Fcp_is_still_charged_when_difal_is_zero()
    {
        var r = DifalCalculator.Calculate(new DifalInput(1000m, 18m, 180m, 18m, 2m, DifalBaseRule.Single));

        Assert.Equal(0m, r.Value);
        Assert.Equal(20m, r.FcpValue);
    }
}
