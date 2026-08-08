using SiagroB1.Commons.Scales;

namespace SiagroB1.Application.Tests.Scales;

public class CaptureStoreTests
{
    private static readonly DateTime T0 = new(2026, 8, 7, 10, 0, 0);

    private static CaptureStore Store() => new(TimeSpan.FromMinutes(10));

    [Fact]
    public void A_capture_can_be_consumed_once()
    {
        var store = Store();
        var capture = store.Create("TS01", 32000, "joao", T0);

        var consumed = store.Consume(capture.CaptureId, "joao", T0.AddMinutes(1));

        Assert.NotNull(consumed);
        Assert.Equal(32000, consumed!.Weight);
        Assert.Equal("TS01", consumed.ScaleCode);
    }

    [Fact]
    public void A_capture_cannot_be_consumed_twice()
    {
        var store = Store();
        var capture = store.Create("TS01", 32000, "joao", T0);

        store.Consume(capture.CaptureId, "joao", T0);

        Assert.Null(store.Consume(capture.CaptureId, "joao", T0));
    }

    [Fact]
    public void An_expired_capture_is_refused()
    {
        var store = Store();
        var capture = store.Create("TS01", 32000, "joao", T0);

        Assert.Null(store.Consume(capture.CaptureId, "joao", T0.AddMinutes(11)));
    }

    [Fact]
    public void A_capture_of_another_user_is_refused()
    {
        var store = Store();
        var capture = store.Create("TS01", 32000, "joao", T0);

        Assert.Null(store.Consume(capture.CaptureId, "maria", T0));
    }

    [Fact]
    public void An_unknown_capture_is_refused()
    {
        Assert.Null(Store().Consume(Guid.NewGuid(), "joao", T0));
    }
}
