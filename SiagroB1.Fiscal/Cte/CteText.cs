using SiagroB1.Fiscal.Dfe;

namespace SiagroB1.Fiscal.Cte;

/// <summary>Higienização de texto livre do CT-e — fachada de <see cref="DfeText"/>.</summary>
public static class CteText
{
    /// <inheritdoc cref="DfeText.Clean"/>
    public static string Clean(string? value, int maxLength = int.MaxValue) => DfeText.Clean(value, maxLength);

    /// <inheritdoc cref="DfeText.CleanOrNull"/>
    public static string? CleanOrNull(string? value, int maxLength = int.MaxValue) => DfeText.CleanOrNull(value, maxLength);
}
