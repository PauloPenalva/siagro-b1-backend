namespace SiagroB1.Web.Sockets.TruckScale;

/// <summary>Envelope das mensagens trocadas com o SiagroB1.Client.</summary>
public sealed class ScaleMessage
{
    public string? Action { get; set; }

    public ScaleMessageData? Data { get; set; }
}

public sealed class ScaleMessageData
{
    public int? Weight { get; set; }

    public bool? Online { get; set; }

    public string? RawFrame { get; set; }
}

/// <summary>Configuração enviada ao Client assim que ele conecta.</summary>
public sealed class ScaleConfigPayload
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
}
