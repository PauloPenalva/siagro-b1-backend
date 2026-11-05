using SiagroB1.Application.Services.Market.Cma;
using Xunit;

namespace SiagroB1.Application.Tests.Market;

public class CmaValueParserTests
{
    [Theory]
    [InlineData("1188.25S", 1188.25, true)]
    [InlineData("1189.5", 1189.5, false)]
    [InlineData("-1.16", -1.16, false)]
    [InlineData(" 12.5 ", 12.5, false)]
    public void Parse_ValidValue_ReturnsNumberAndSettlementFlag(string raw, double expected, bool settlement)
    {
        var result = CmaValueParser.Parse(raw);

        Assert.Equal((decimal)expected, result.Value);
        Assert.Equal(settlement, result.IsSettlement);
    }

    [Theory]
    [InlineData("-")]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    [InlineData("abc")]
    [InlineData("S")]
    [InlineData("1,5")]
    public void Parse_NoValueOrInvalid_ReturnsNullNeverZero(string? raw)
    {
        var result = CmaValueParser.Parse(raw);

        Assert.Null(result.Value);
        Assert.False(result.IsSettlement);
    }
}
