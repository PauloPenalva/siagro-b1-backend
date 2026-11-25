using SiagroB1.Web.Functions.ShipmentLoads;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// Parâmetro <c>SapDocEntries</c> da função <c>ShipmentLoadsGetSapLinkPreview</c> (GAC-1226): string OData com os
/// DocEntry separados por vírgula.
/// </summary>
public class SapLinkPreviewDocEntriesParserTests
{
    [Theory]
    [InlineData("5", new[] { 5 })]
    [InlineData("5,7", new[] { 5, 7 })]
    [InlineData("'5,7'", new[] { 5, 7 })]
    [InlineData(" ' 5 , 7 ' ", new[] { 5, 7 })]
    public void Parses_a_valid_list(string raw, int[] expected)
    {
        Assert.True(ShipmentLoadsGetSapLinkPreviewController.TryParseDocEntries(raw, out var entries));
        Assert.Equal(expected, entries);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("''")]
    [InlineData("   ")]
    [InlineData("5,")]
    [InlineData("5,,7")]
    [InlineData("abc")]
    [InlineData("5,x")]
    [InlineData("1.5")]
    [InlineData("0")]
    [InlineData("5,0")]
    [InlineData("-3")]
    [InlineData("99999999999")]
    public void Rejects_empty_non_numeric_zero_and_negative(string? raw)
    {
        Assert.False(ShipmentLoadsGetSapLinkPreviewController.TryParseDocEntries(raw, out var entries));
        Assert.Empty(entries);
    }
}
