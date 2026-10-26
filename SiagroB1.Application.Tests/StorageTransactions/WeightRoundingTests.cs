using SiagroB1.Application.Services.StorageTransactions;
using Xunit;

namespace SiagroB1.Application.Tests.StorageTransactions;

// GAC-1205: pesos dos romaneios de armazenagem arredondados para kg inteiro, "Round Half Up".
public class WeightRoundingTests
{
    [Theory]
    [InlineData(123.4, 123)]
    [InlineData(123.49, 123)]
    [InlineData(123.5, 124)]
    [InlineData(122.5, 123)]   // ToEven daria 122
    [InlineData(123.6, 124)]
    [InlineData(0, 0)]
    public void ToKilograms_RoundsHalfUp(decimal value, decimal expected)
    {
        Assert.Equal(expected, WeightRounding.ToKilograms(value));
    }
}
