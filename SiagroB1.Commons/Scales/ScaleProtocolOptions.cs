namespace SiagroB1.Commons.Scales;

/// <summary>
/// Parâmetros de leitura de um modelo de balança. Os valores padrão são o preset do Jundiaí
/// BJ850 (ASCII contínuo, terminador CR/LF, seis dígitos a partir da posição 1); o cadastro da
/// balança sobrescreve o que for diferente, sem exigir recompilação.
/// </summary>
public sealed class ScaleProtocolOptions
{
    public string Protocol { get; init; } = "JundiaiBj850";

    public int FramePrefixLength { get; init; } = 1;

    public int WeightLength { get; init; } = 6;

    public int DecimalPlaces { get; init; }

    public string FrameTerminator { get; init; } = "\n";

    /// <summary>Expressão regular do protocolo genérico. O peso sai do grupo "weight" ou do grupo 1.</summary>
    public string? FramePattern { get; init; }
}
