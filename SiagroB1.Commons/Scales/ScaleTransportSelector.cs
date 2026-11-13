namespace SiagroB1.Commons.Scales;

/// <summary>
/// Decide o transporte pelo tipo de conexão que o servidor manda no scale_config. Tudo que não for
/// "Serial" cai em TCP/IP - inclusive campo ausente, que é o servidor anterior a esta opção.
/// </summary>
public static class ScaleTransportSelector
{
    public const string Serial = "Serial";

    public static bool IsSerial(string? connectionType) =>
        string.Equals(connectionType?.Trim(), Serial, StringComparison.OrdinalIgnoreCase);
}
