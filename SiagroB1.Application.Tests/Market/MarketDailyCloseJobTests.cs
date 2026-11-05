using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Jobs;
using SiagroB1.Application.Services.Market;
using SiagroB1.Application.Tests.Support;
using static SiagroB1.Application.Tests.Market.MarketTestHelpers;

namespace SiagroB1.Application.Tests.Market;

public class MarketDailyCloseJobTests
{
    [Theory]
    [InlineData("2026-11-05T21:00:00Z", "2026-11-05")]
    [InlineData("2026-11-06T01:00:00Z", "2026-11-05")]
    public async Task Execute_ClosesTheSaoPauloLocalDate(string nowUtc, string expectedDate)
    {
        var db = await SeedAsync(Instrument("ZSF7"));
        await QuoteService(db).ApplyCmaUpdatesAsync([Update("ZSF7", 1100m)], CancellationToken.None);
        var job = new MarketDailyCloseJob(new MarketDailyCloseService(db.Context, new TestLogger<MarketDailyCloseService>()),
            new ManualTimeProvider(DateTimeOffset.Parse(nowUtc)), new TestLogger<MarketDailyCloseJob>());

        await job.ExecuteAsync(CancellationToken.None);

        var row = await db.Context.MarketDailyCloses.AsNoTracking().SingleAsync();
        Assert.Equal(DateOnly.Parse(expectedDate).ToDateTime(TimeOnly.MinValue), row.Date);
        Assert.Equal(1100m, row.Close);
    }
}
