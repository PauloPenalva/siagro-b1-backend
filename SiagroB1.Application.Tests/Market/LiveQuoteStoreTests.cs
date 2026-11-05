using SiagroB1.Commons.Market;

namespace SiagroB1.Application.Tests.Market;

public class LiveQuoteStoreTests
{
    [Fact]
    public void NotifyChanged_IsolatedPerCompany()
    {
        var store = new LiveQuoteStore();

        store.NotifyChanged("A");

        Assert.Equal(1, store.Get("A").Version);
        Assert.Equal(0, store.Get("B").Version);
    }

    [Fact]
    public void NotifyChanged_IncrementsVersion()
    {
        var store = new LiveQuoteStore();

        store.NotifyChanged("A");
        store.NotifyChanged("A");

        var snapshot = store.Get("A");
        Assert.Equal(2, snapshot.Version);
        Assert.NotNull(snapshot.LastChangedAt);
    }

    [Fact]
    public void SetStatus_Visible_InSnapshot()
    {
        var store = new LiveQuoteStore();

        store.SetStatus("A", CmaConnectionStatus.Error, "Falha de login");

        var snapshot = store.Get("A");
        Assert.Equal(CmaConnectionStatus.Error, snapshot.Status);
        Assert.Equal("Falha de login", snapshot.Message);
        Assert.Equal(1, snapshot.Version);
    }

    [Fact]
    public void Get_UnknownCompany_IsDisabledAtVersionZero()
    {
        var snapshot = new LiveQuoteStore().Get("X");

        Assert.Equal(new LiveQuoteSnapshot(0, CmaConnectionStatus.Disabled, null, null), snapshot);
    }

    [Fact]
    public void TryConsumeReconnect_ReturnsTrueOnce()
    {
        var store = new LiveQuoteStore();
        Assert.False(store.TryConsumeReconnect("A"));

        store.RequestReconnect("A");

        Assert.True(store.TryConsumeReconnect("A"));
        Assert.False(store.TryConsumeReconnect("A"));
    }
}
