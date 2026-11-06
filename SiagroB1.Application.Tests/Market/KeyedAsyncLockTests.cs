using SiagroB1.Application.Services.Market.Tickets;

namespace SiagroB1.Application.Tests.Market;

public class KeyedAsyncLockTests
{
    [Fact]
    public async Task SameKey_Serializes()
    {
        var locks = new KeyedAsyncLock();
        var first = await locks.AcquireAsync("a");

        var second = locks.AcquireAsync("a");
        await Task.Delay(50);
        Assert.False(second.IsCompleted);

        first.Dispose();
        (await second.WaitAsync(TimeSpan.FromSeconds(5))).Dispose();
        Assert.Equal(0, locks.ActiveKeys);
    }

    [Fact]
    public async Task DifferentKeys_DoNotBlock()
    {
        var locks = new KeyedAsyncLock();
        using var a = await locks.AcquireAsync("a");

        var b = locks.AcquireAsync("b");
        Assert.True(b.IsCompleted);
        (await b).Dispose();
        Assert.Equal(1, locks.ActiveKeys);
    }

    [Fact]
    public async Task CanceledWait_DoesNotLeakKey()
    {
        var locks = new KeyedAsyncLock();
        var held = await locks.AcquireAsync("a");
        using var cts = new CancellationTokenSource(20);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => locks.AcquireAsync("a", cts.Token));

        held.Dispose();
        Assert.Equal(0, locks.ActiveKeys);
    }
}
