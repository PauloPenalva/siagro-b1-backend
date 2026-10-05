using SiagroB1.Application.Services.Taxes;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.Taxes;

public class IbsCbsRatesServiceTests
{
    private static IbsCbsRate Rate(int year, decimal cbs, decimal ibsState, decimal ibsMun = 0m) => new()
    {
        StartDate = new DateOnly(year, 1, 1), CbsRate = cbs, IbsStateRate = ibsState, IbsMunicipalRate = ibsMun,
    };

    [Fact]
    public async Task Effective_rate_is_the_latest_start_date_on_or_before_the_date()
    {
        var db = TestDb.CreateUnitOfWork();
        var service = new IbsCbsRatesService(db);
        await service.CreateAsync(Rate(2026, 0.9m, 0.1m));
        await service.CreateAsync(Rate(2027, 8.8m, 0.05m, 0.05m));

        Assert.Equal(0.9m, (await service.GetEffectiveAsync(new DateOnly(2026, 12, 31)))!.CbsRate);
        Assert.Equal(8.8m, (await service.GetEffectiveAsync(new DateOnly(2027, 1, 1)))!.CbsRate);
        Assert.Null(await service.GetEffectiveAsync(new DateOnly(2025, 12, 31)));
    }

    [Fact]
    public async Task Start_date_is_unique()
    {
        var db = TestDb.CreateUnitOfWork();
        var service = new IbsCbsRatesService(db);
        await service.CreateAsync(Rate(2026, 0.9m, 0.1m));

        var ex = await Assert.ThrowsAsync<DefaultException>(() => service.CreateAsync(Rate(2026, 1m, 1m)));
        Assert.Contains("vigência", ex.Message);
    }

    [Fact]
    public async Task Rates_must_be_between_0_and_100()
    {
        var db = TestDb.CreateUnitOfWork();
        await Assert.ThrowsAsync<DefaultException>(() =>
            new IbsCbsRatesService(db).CreateAsync(Rate(2026, -1m, 0.1m)));
    }

    [Fact]
    public async Task Update_and_delete_work()
    {
        var db = TestDb.CreateUnitOfWork();
        var service = new IbsCbsRatesService(db);
        var created = await service.CreateAsync(Rate(2026, 0.9m, 0.1m));

        created.CbsRate = 1m;
        await service.UpdateAsync(created.Key, created);
        Assert.Equal(1m, (await service.GetByIdAsync(created.Key))!.CbsRate);

        Assert.True(await service.DeleteAsync(created.Key));
        Assert.Null(await service.GetByIdAsync(created.Key));
    }
}
