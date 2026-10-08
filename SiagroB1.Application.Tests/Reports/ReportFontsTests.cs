using System.Text.RegularExpressions;
using SiagroB1.Reports.Services;

namespace SiagroB1.Application.Tests.Reports;

/// <summary>
/// No Linux o FontManager do FastReport procura a fonte pelo nome exato e, sem achar, cai na fonte
/// padrão (o alias do fontconfig não vale). Toda fonte usada num .frx precisa de substituto registrado.
/// </summary>
public class ReportFontsTests
{
    [Fact]
    public void Every_font_used_by_a_template_has_a_substitute()
    {
        var templates = Directory.GetFiles(AppContext.BaseDirectory, "*.frx", SearchOption.AllDirectories);
        var used = templates
            .SelectMany(file => Regex.Matches(File.ReadAllText(file), "Font=\"([^\",]+)").Select(m => m.Groups[1].Value))
            .ToHashSet();

        Assert.NotEmpty(templates);
        Assert.Contains("Times New Roman", used);
        Assert.Empty(used.Where(font => !ReportFonts.Substitutes.ContainsKey(font)));
    }

    [Fact]
    public void Registering_is_idempotent()
    {
        ReportFonts.RegisterSubstitutes();
        ReportFonts.RegisterSubstitutes();
    }
}
