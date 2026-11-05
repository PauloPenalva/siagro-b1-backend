using System.Text.Json;
using SiagroB1.Application.Services.Market.Cma;
using Xunit;

namespace SiagroB1.Application.Tests.Market;

public class CmaQuoteMapperTests
{
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Map_FlatShape_MapsAllFields()
    {
        var update = CmaQuoteMapper.Map(Json(
            """{"symbol":"ZSF7","last10":"1188.25S","bid14":"1189.5","percentageChange01":"-1.16","high16":"1210","low17":"1186.75","open18":"1202.25"}"""));

        Assert.NotNull(update);
        Assert.Equal("ZSF7", update!.Symbol);
        Assert.Equal(1188.25m, update.Last);
        Assert.True(update.IsSettlement);
        Assert.Equal(1189.5m, update.Bid);
        Assert.Equal(-1.16m, update.ChangePercent);
        Assert.Equal(1210m, update.High);
        Assert.Equal(1186.75m, update.Low);
        Assert.Equal(1202.25m, update.Open);
        Assert.Null(update.Ask);
        Assert.Null(update.PreviousClose);
        Assert.Null(update.QuotedAt);
    }

    [Fact]
    public void Map_WireShape_MapsSymbolIdAndArrValuesByHexKey()
    {
        var update = CmaQuoteMapper.Map(Json(
            """{"name":"QuotesUpdateResponse","datetime":"2026-11-05 11:30:15.123","symbolId":{"sourceId":"1","symbol":"ZSF7"},"arrValues":[{"10":"1188.25S"},{"14":"1189.5"},{"03":"1190"},{"01":"-1.16"},{"16":"1210"},{"17":"-"},{"18":"1202.25"},{"1A":"1200"}]}"""));

        Assert.NotNull(update);
        Assert.Equal("ZSF7", update!.Symbol);
        Assert.Equal(1188.25m, update.Last);
        Assert.True(update.IsSettlement);
        Assert.Equal(1189.5m, update.Bid);
        Assert.Equal(1190m, update.Ask);
        Assert.Equal(-1.16m, update.ChangePercent);
        Assert.Equal(1210m, update.High);
        Assert.Null(update.Low);
        Assert.Equal(1202.25m, update.Open);
        Assert.Equal(1200m, update.PreviousClose);
        Assert.Equal(new DateTime(2026, 11, 5, 14, 30, 15, 123, DateTimeKind.Utc), update.QuotedAt);
    }

    [Fact]
    public void Map_Datetime_IsSaoPauloLocalTimeConvertedToUtc()
    {
        var update = CmaQuoteMapper.Map(Json("""{"symbol":"X","datetime":"2026-11-05 15:30:00.000"}"""));

        Assert.Equal(new DateTime(2026, 11, 5, 18, 30, 0, DateTimeKind.Utc), update!.QuotedAt);
        Assert.Equal(DateTimeKind.Utc, update.QuotedAt!.Value.Kind);
    }

    [Fact]
    public void Map_WithoutSymbol_ReturnsNull()
    {
        Assert.Null(CmaQuoteMapper.Map(Json("""{"last10":"1"}""")));
        Assert.Null(CmaQuoteMapper.Map(Json("""{"symbolId":{"sourceId":"1"},"arrValues":[]}""")));
        Assert.Null(CmaQuoteMapper.Map(Json("[]")));
    }

    [Fact]
    public void Map_InvalidDatetime_LeavesQuotedAtNull()
    {
        var update = CmaQuoteMapper.Map(Json("""{"symbol":"X","datetime":"garbage"}"""));

        Assert.NotNull(update);
        Assert.Null(update!.QuotedAt);
    }
}
