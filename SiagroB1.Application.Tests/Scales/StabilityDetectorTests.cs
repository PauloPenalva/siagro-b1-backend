using SiagroB1.Commons.Scales;

namespace SiagroB1.Application.Tests.Scales;

public class StabilityDetectorTests
{
    private static readonly DateTime T0 = new(2026, 8, 7, 10, 0, 0);

    private static StabilityDetector Detector() =>
        new(TimeSpan.FromSeconds(3), minimumSamples: 5);

    [Fact]
    public void Is_not_stable_before_the_minimum_number_of_samples()
    {
        var detector = Detector();

        for (var i = 0; i < 4; i++)
            detector.Add(20000, T0.AddMilliseconds(250 * i));

        Assert.False(detector.IsStable(T0.AddMilliseconds(1000)));
    }

    [Fact]
    public void Is_stable_after_enough_equal_samples_inside_the_window()
    {
        var detector = Detector();

        for (var i = 0; i < 6; i++)
            detector.Add(20000, T0.AddMilliseconds(250 * i));

        Assert.True(detector.IsStable(T0.AddMilliseconds(1250)));
        Assert.Equal(20000, detector.Current);
    }

    [Fact]
    public void A_different_reading_restarts_the_window()
    {
        var detector = Detector();

        for (var i = 0; i < 6; i++)
            detector.Add(20000, T0.AddMilliseconds(250 * i));

        detector.Add(20040, T0.AddMilliseconds(1500));

        Assert.False(detector.IsStable(T0.AddMilliseconds(1500)));
        Assert.Equal(20040, detector.Current);
    }

    [Fact]
    public void Samples_older_than_the_window_are_dropped()
    {
        var detector = Detector();

        detector.Add(19000, T0);

        for (var i = 1; i <= 6; i++)
            detector.Add(20000, T0.AddSeconds(4).AddMilliseconds(250 * i));

        Assert.True(detector.IsStable(T0.AddSeconds(6)));
    }
}
