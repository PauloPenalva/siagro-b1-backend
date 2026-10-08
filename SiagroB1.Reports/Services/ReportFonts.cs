using FastReport;

namespace SiagroB1.Reports.Services;

/// <summary>
/// Substitutos das fontes dos .frx para servidores Linux. O <see cref="FontManager"/> do FastReport
/// procura a família pelo nome EXATO entre as instaladas e, sem achar, usa
/// <c>FontFamily.GenericSansSerif</c> (DejaVu Sans, bem mais larga): o texto estoura as caixas. O alias
/// do fontconfig ("Times New Roman" → Liberation Serif) não ajuda, porque a busca é por nome na lista
/// de famílias. No Windows a fonte original existe e o substituto nunca é consultado.
///
/// O servidor precisa ter as fontes Liberation (<c>apt install fonts-liberation</c>), que têm a mesma
/// métrica de Times New Roman/Arial/Courier New; DejaVu é o último recurso. Fonte nova num .frx
/// precisa entrar aqui (o teste ReportFontsTests acusa).
/// </summary>
public static class ReportFonts
{
    public static readonly IReadOnlyDictionary<string, string[]> Substitutes = new Dictionary<string, string[]>
    {
        ["Times New Roman"] = ["Liberation Serif", "Tinos", "DejaVu Serif"],
        ["Arial"] = ["Liberation Sans", "Arimo", "DejaVu Sans"],
        ["Tahoma"] = ["Liberation Sans", "Arimo", "DejaVu Sans"],
        ["Courier New"] = ["Liberation Mono", "Cousine", "DejaVu Sans Mono"],
        ["Consolas"] = ["Liberation Mono", "Cousine", "DejaVu Sans Mono"],
    };

    /// <summary>Chamado uma vez no startup do Reports; idempotente.</summary>
    public static void RegisterSubstitutes()
    {
        foreach (var (font, substitutes) in Substitutes)
        {
            FontManager.RemoveSubstituteFont(font);
            FontManager.AddSubstituteFont(font, substitutes);
        }
    }
}
