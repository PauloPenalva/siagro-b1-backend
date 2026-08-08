using SiagroB1.Commons.Scales;

namespace SiagroB1.Client.Dtos;

public sealed class ScaleConfigMessage
{
    public string? Action { get; set; }

    public ScaleConfigData? Data { get; set; }
}

public sealed class ScaleConfigData
{
    public string? Ip { get; set; }

    public int Port { get; set; }

    public string Protocol { get; set; } = "JundiaiBj850";

    public int FramePrefixLength { get; set; } = 1;

    public int WeightLength { get; set; } = 6;

    public int DecimalPlaces { get; set; }

    public string FrameTerminator { get; set; } = "\n";

    public string? FramePattern { get; set; }

    public bool LogRawFrames { get; set; }

    public ScaleProtocolOptions ToOptions() => new()
    {
        Protocol = Protocol,
        FramePrefixLength = FramePrefixLength,
        WeightLength = WeightLength,
        DecimalPlaces = DecimalPlaces,
        FrameTerminator = FrameTerminator,
        FramePattern = FramePattern
    };
}
