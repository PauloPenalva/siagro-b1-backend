using SiagroB1.Commons.Scales;

namespace SiagroB1.Application.Tests.Scales;

public class ScaleProtocolTests
{
    private static ScaleProtocolOptions Bj850() => new();

    [Fact]
    public void Bj850_parses_the_six_digits_after_the_prefix()
    {
        var protocol = ScaleProtocolFactory.Create(Bj850());

        Assert.True(protocol.TryParse("=012345", out var weight));
        Assert.Equal(12345, weight);
    }

    [Fact]
    public void Bj850_ignores_a_trailing_carriage_return()
    {
        var protocol = ScaleProtocolFactory.Create(Bj850());

        Assert.True(protocol.TryParse("=012345\r", out var weight));
        Assert.Equal(12345, weight);
    }

    [Fact]
    public void Bj850_rejects_a_truncated_frame()
    {
        var protocol = ScaleProtocolFactory.Create(Bj850());

        Assert.False(protocol.TryParse("=0123", out _));
    }

    [Fact]
    public void Bj850_rejects_a_frame_with_non_digits_in_the_weight()
    {
        var protocol = ScaleProtocolFactory.Create(Bj850());

        Assert.False(protocol.TryParse("=01A345", out _));
    }

    [Fact]
    public void Bj850_reads_a_negative_weight()
    {
        var protocol = ScaleProtocolFactory.Create(Bj850());

        Assert.True(protocol.TryParse("=-01234", out var weight));
        Assert.Equal(-1234, weight);
    }

    [Fact]
    public void Decimal_places_are_rounded_to_whole_kilos()
    {
        var protocol = ScaleProtocolFactory.Create(new ScaleProtocolOptions { DecimalPlaces = 1 });

        Assert.True(protocol.TryParse("=012345", out var weight));
        Assert.Equal(1235, weight);
    }

    [Fact]
    public void Prefix_and_length_overrides_are_honoured()
    {
        var options = new ScaleProtocolOptions { FramePrefixLength = 3, WeightLength = 5 };
        var protocol = ScaleProtocolFactory.Create(options);

        Assert.True(protocol.TryParse("STX09876kg", out var weight));
        Assert.Equal(9876, weight);
    }

    [Fact]
    public void Generic_protocol_extracts_the_named_group()
    {
        var options = new ScaleProtocolOptions
        {
            Protocol = "Generic",
            FramePattern = @"PESO:\s*(?<weight>-?\d+)"
        };
        var protocol = ScaleProtocolFactory.Create(options);

        Assert.True(protocol.TryParse("PESO: 24680 KG", out var weight));
        Assert.Equal(24680, weight);
    }

    [Fact]
    public void Generic_protocol_rejects_a_frame_without_a_match()
    {
        var options = new ScaleProtocolOptions
        {
            Protocol = "Generic",
            FramePattern = @"PESO:\s*(?<weight>-?\d+)"
        };
        var protocol = ScaleProtocolFactory.Create(options);

        Assert.False(protocol.TryParse("SEM LEITURA", out _));
    }

    [Fact]
    public void Empty_frames_are_rejected_by_both_protocols()
    {
        Assert.False(ScaleProtocolFactory.Create(Bj850()).TryParse("", out _));
        Assert.False(ScaleProtocolFactory
            .Create(new ScaleProtocolOptions { Protocol = "Generic", FramePattern = @"(\d+)" })
            .TryParse("   ", out _));
    }
}
