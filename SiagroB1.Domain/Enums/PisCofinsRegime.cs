namespace SiagroB1.Domain.Enums;

/// <summary>Regime de apuração do PIS/COFINS da filial.</summary>
public enum PisCofinsRegime
{
    /// <summary>Cumulativo (Lucro Presumido), padrão.</summary>
    Cumulative = 0,

    /// <summary>Não cumulativo (Lucro Real).</summary>
    NonCumulative = 1,
}
