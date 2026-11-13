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

    /// <summary>"Tcp" ou "Serial". Client antigo ignora o campo e segue em TCP.</summary>
    public string ConnectionType { get; set; } = "Tcp";

    public string? SerialPortName { get; set; }

    public int BaudRate { get; set; } = 9600;

    public int DataBits { get; set; } = 8;

    /// <summary>"None", "Odd" ou "Even".</summary>
    public string Parity { get; set; } = "None";

    public int StopBits { get; set; } = 1;

    public string Protocol { get; set; } = "JundiaiBj850";

    public int FramePrefixLength { get; set; } = 1;

    public int WeightLength { get; set; } = 6;

    public int DecimalPlaces { get; set; }

    public string FrameTerminator { get; set; } = "\n";

    public string? FramePattern { get; set; }

    public bool LogRawFrames { get; set; }
}
