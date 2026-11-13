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

    /// <summary>"Tcp" ou "Serial". Servidor antigo não manda o campo: fica em TCP/IP.</summary>
    public string ConnectionType { get; set; } = "Tcp";

    public string? SerialPortName { get; set; }

    public int BaudRate { get; set; } = 9600;

    public int DataBits { get; set; } = 8;

    public string Parity { get; set; } = "None";

    public int StopBits { get; set; } = 1;

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
