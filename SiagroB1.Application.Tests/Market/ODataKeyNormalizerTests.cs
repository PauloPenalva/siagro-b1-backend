using SiagroB1.Web.Market;

namespace SiagroB1.Application.Tests.Market;

public class ODataKeyNormalizerTests
{
    [Theory]
    [InlineData("DOL%20COM", "DOL COM")]
    [InlineData("'DOL%20COM'", "DOL COM")]
    [InlineData("DOL COM", "DOL COM")]
    [InlineData("O''X", "O'X")]
    [InlineData("'O''X'", "O'X")]
    [InlineData("ZSF7", "ZSF7")]
    [InlineData("'ZSF7'", "ZSF7")]
    public void Normalize_DecodesAndUnquotes(string raw, string expected) =>
        Assert.Equal(expected, ODataKeyNormalizer.Normalize(raw));
}
