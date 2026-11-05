using System.Globalization;

namespace SiagroB1.Application.Services.Market.Cma;

/// <summary>Valor numérico da CMA; <see cref="Value"/> nulo significa "sem valor" (nunca zero).</summary>
public readonly record struct CmaValue(decimal? Value, bool IsSettlement);

public static class CmaValueParser
{
    /// <summary>
    /// Interpreta o texto da CMA: sufixo "S" = preço de ajuste; "-", vazio, nulo ou ilegível = sem valor.
    /// </summary>
    public static CmaValue Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return default;

        var text = raw.Trim();
        var settlement = text.EndsWith('S') || text.EndsWith('s');
        if (settlement)
            text = text[..^1].TrimEnd();

        if (text.Length == 0 || text == "-")
            return default;

        return decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? new CmaValue(value, settlement)
            : default;
    }
}
