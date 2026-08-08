using SiagroB1.Commons.Scales;

namespace SiagroB1.Application.Tests.Scales;

public class LiveReadingStoreTests
{
    private static readonly DateTime T0 = new(2026, 8, 7, 10, 0, 0);

    private static LiveReadingStore Store() =>
        new(TimeSpan.FromSeconds(3), minimumSamples: 5, offlineAfter: TimeSpan.FromSeconds(2));

    [Fact]
    public void An_unknown_scale_is_offline()
    {
        var live = Store().Get("TS01", T0);

        Assert.False(live.Online);
        Assert.False(live.Stable);
        Assert.Equal(0, live.Weight);
    }

    [Fact]
    public void Reports_the_last_weight_while_readings_keep_arriving()
    {
        var store = Store();

        store.Push("TS01", 18000, T0);
        store.Push("TS01", 18500, T0.AddMilliseconds(250));

        var live = store.Get("TS01", T0.AddMilliseconds(300));

        Assert.True(live.Online);
        Assert.False(live.Stable);
        Assert.Equal(18500, live.Weight);
    }

    [Fact]
    public void Becomes_stable_after_enough_equal_readings()
    {
        var store = Store();

        for (var i = 0; i < 6; i++)
            store.Push("TS01", 32000, T0.AddMilliseconds(250 * i));

        var live = store.Get("TS01", T0.AddMilliseconds(1300));

        Assert.True(live.Stable);
        Assert.Equal(32000, live.Weight);
    }

    [Fact]
    public void Goes_offline_when_readings_stop_arriving()
    {
        var store = Store();

        for (var i = 0; i < 6; i++)
            store.Push("TS01", 32000, T0.AddMilliseconds(250 * i));

        var live = store.Get("TS01", T0.AddSeconds(10));

        Assert.False(live.Online);
        Assert.False(live.Stable);
    }

    [Fact]
    public void Set_offline_clears_the_reading_immediately()
    {
        var store = Store();

        store.Push("TS01", 32000, T0);
        store.SetOffline("TS01");

        Assert.False(store.Get("TS01", T0).Online);
    }

    [Fact]
    public void Scales_do_not_interfere_with_each_other()
    {
        var store = Store();

        store.Push("TS01", 10000, T0);
        store.Push("TS02", 20000, T0);

        Assert.Equal(10000, store.Get("TS01", T0).Weight);
        Assert.Equal(20000, store.Get("TS02", T0).Weight);
    }
}
