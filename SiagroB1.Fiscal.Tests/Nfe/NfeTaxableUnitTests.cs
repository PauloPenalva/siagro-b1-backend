using SiagroB1.Fiscal.Nfe;

namespace SiagroB1.Fiscal.Tests.Nfe;

/// <summary>KG da linha → unidade tributável: qTrib com 4 casas, vUnTrib com 10 (limites do leiaute).</summary>
public class NfeTaxableUnitTests
{
    [Fact]
    public void Exact_tons() =>
        Assert.Equal((30m, 2000m), NfeTaxableUnit.Convert(30000m, 60000m, 1000m));

    [Fact]
    public void Quantity_keeps_four_decimals() =>
        Assert.Equal(30.0005m, NfeTaxableUnit.Convert(30000.5m, 60001m, 1000m).Quantity);

    [Fact]
    public void Quantity_is_rounded_to_four_decimals() =>
        Assert.Equal(1.2346m, NfeTaxableUnit.Convert(1234.56789m, 2469.14m, 1000m).Quantity);

    [Fact]
    public void Unit_price_is_the_total_over_the_rounded_quantity_with_ten_decimals()
    {
        var (quantity, unitPrice) = NfeTaxableUnit.Convert(33333m, 100000m, 1000m);

        Assert.Equal(33.333m, quantity);
        Assert.Equal(3000.0300003000m, unitPrice);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.00004)]
    public void Quantity_that_rounds_to_zero_has_zero_unit_price(double quantity) =>
        Assert.Equal((0m, 0m), NfeTaxableUnit.Convert((decimal)quantity, 150m, 1000m));
}
