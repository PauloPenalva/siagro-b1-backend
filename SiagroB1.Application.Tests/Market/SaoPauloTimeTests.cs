using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Time;

namespace SiagroB1.Application.Tests.Market;

public class SaoPauloTimeTests
{
    [Fact]
    public void Resolve_UnknownIds_FallsBackToFixedUtcMinus3WithoutThrowing()
    {
        var zone = SaoPauloTime.Resolve("Nope/Unknown", "Also Unknown");

        Assert.Equal(TimeSpan.FromHours(-3), zone.BaseUtcOffset);
        Assert.Equal(new DateTime(2026, 11, 5, 18, 30, 0, DateTimeKind.Utc),
            Application.Services.Market.Cma.CmaQuoteMapper.ToUtc(new DateTime(2026, 11, 5, 15, 30, 0), zone));
    }

    [Theory]
    [InlineData("2026-11-05T21:00:00Z", "2026-11-05")]
    [InlineData("2026-11-06T01:00:00Z", "2026-11-05")]
    [InlineData("2026-11-06T03:00:00Z", "2026-11-06")]
    public void Today_UsesSaoPauloLocalDate(string utc, string expected) =>
        Assert.Equal(DateOnly.Parse(expected), SaoPauloTime.Today(new ManualTimeProvider(DateTimeOffset.Parse(utc))));
}
