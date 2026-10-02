using SiagroB1.Application.Services.Taxes;

namespace SiagroB1.Application.Tests.Taxes;

public class InterstateIcmsRateTests
{
    [Theory]
    [InlineData("SP", "BA", 0, 7)]
    [InlineData("PR", "ES", 0, 7)]
    [InlineData("MG", "GO", 0, 7)]
    [InlineData("SP", "PR", 0, 12)]
    [InlineData("BA", "SP", 0, 12)]
    [InlineData("ES", "BA", 0, 12)]
    [InlineData("GO", "MT", 0, 12)]
    [InlineData("SP", "BA", 1, 4)]
    [InlineData("SP", "PR", 2, 4)]
    [InlineData("BA", "SP", 3, 4)]
    [InlineData("SP", "PR", 8, 4)]
    [InlineData("SP", "BA", 6, 7)]
    [InlineData("SP", "PR", 7, 12)]
    [InlineData("SP", "PR", 5, 12)]
    public void Resolves_the_senate_rate(string origin, string destination, byte goodsOrigin, int expected) =>
        Assert.Equal(expected, InterstateIcmsRate.Resolve(origin, destination, goodsOrigin));

    [Fact]
    public void State_comparison_ignores_case() =>
        Assert.Equal(7m, InterstateIcmsRate.Resolve("sp", "ba", 0));
}
