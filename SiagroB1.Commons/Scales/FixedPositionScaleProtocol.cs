using System.Globalization;

namespace SiagroB1.Commons.Scales;

/// <summary>
/// Peso em posição e tamanho fixos dentro do frame - o formato do Jundiaí BJ850 e da maioria dos
/// indicadores nacionais. O caractere de sinal, quando existe, ocupa a primeira posição do campo.
/// </summary>
public sealed class FixedPositionScaleProtocol(ScaleProtocolOptions options) : IScaleProtocol
{
    public bool TryParse(string frame, out int weightKg)
    {
        weightKg = 0;

        var clean = frame.Trim('\r', '\n', '\0', ' ');

        if (clean.Length < options.FramePrefixLength + options.WeightLength)
            return false;

        var field = clean.Substring(options.FramePrefixLength, options.WeightLength);

        var negative = field.StartsWith('-');
        var digits = negative ? field[1..] : field;

        if (digits.Length == 0 || !digits.All(char.IsAsciiDigit))
            return false;

        if (!long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var raw))
            return false;

        weightKg = Scale(raw, negative, options.DecimalPlaces);
        return true;
    }

    /// <summary>
    /// O peso trafega e é gravado em quilos inteiros: aplicar as casas decimais aqui é o que
    /// permite comparar o comprovante de captura com o valor da ação por igualdade exata.
    /// </summary>
    internal static int Scale(long raw, bool negative, int decimalPlaces)
    {
        var divisor = Math.Pow(10, decimalPlaces);
        var value = (int)Math.Round(raw / divisor, MidpointRounding.AwayFromZero);

        return negative ? -value : value;
    }
}
