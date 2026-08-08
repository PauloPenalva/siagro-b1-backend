using System.Globalization;
using System.Text.RegularExpressions;

namespace SiagroB1.Commons.Scales;

/// <summary>Protocolo genérico, para o próximo modelo de balança que não couber no de posição fixa.</summary>
public sealed class RegexScaleProtocol : IScaleProtocol
{
    private readonly Regex _pattern;
    private readonly int _decimalPlaces;

    public RegexScaleProtocol(ScaleProtocolOptions options)
    {
        var pattern = string.IsNullOrWhiteSpace(options.FramePattern)
            ? @"(?<weight>-?\d+)"
            : options.FramePattern;

        _pattern = new Regex(pattern, RegexOptions.Compiled, TimeSpan.FromMilliseconds(50));
        _decimalPlaces = options.DecimalPlaces;
    }

    public bool TryParse(string frame, out int weightKg)
    {
        weightKg = 0;

        var match = _pattern.Match(frame ?? string.Empty);
        if (!match.Success)
            return false;

        var group = match.Groups["weight"].Success ? match.Groups["weight"] : match.Groups[1];
        if (!group.Success)
            return false;

        var text = group.Value;
        var negative = text.StartsWith('-');
        var digits = negative ? text[1..] : text;

        if (!long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var raw))
            return false;

        weightKg = FixedPositionScaleProtocol.Scale(raw, negative, _decimalPlaces);
        return true;
    }
}
