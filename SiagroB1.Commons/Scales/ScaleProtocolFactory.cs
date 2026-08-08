namespace SiagroB1.Commons.Scales;

public static class ScaleProtocolFactory
{
    public static IScaleProtocol Create(ScaleProtocolOptions options) =>
        string.Equals(options.Protocol, "Generic", StringComparison.OrdinalIgnoreCase)
            ? new RegexScaleProtocol(options)
            : new FixedPositionScaleProtocol(options);
}
