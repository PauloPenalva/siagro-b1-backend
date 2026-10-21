using System.Text;
using System.Text.RegularExpressions;
using FastReport;
using FastReport.Export.PdfSimple;
using SiagroB1.Reports.Services;

namespace SiagroB1.Application.Tests.Reports;

/// <summary>
/// O exportador reaproveita um único Bitmap entre as páginas (o libgdiplus do Linux não devolve a memória de
/// cada Bitmap criado). Precisa produzir o mesmo PDF raster que o PDFSimpleExport, sem "vazar" o desenho de
/// uma página para a seguinte.
/// </summary>
public class ReusableBitmapPdfExportTests
{
    static ReusableBitmapPdfExportTests()
    {
        FastReport.Utils.Config.WebMode = true;
    }

    private static Report Prepared(params (string Text, bool Landscape)[] pages)
    {
        var report = new Report();
        for (var i = 0; i < pages.Length; i++)
        {
            var page = new ReportPage { Name = $"Page{i}", Landscape = pages[i].Landscape };
            report.Pages.Add(page);
            page.ReportTitle = new ReportTitleBand { Name = $"Title{i}", Height = 60 };
            page.ReportTitle.Objects.Add(new TextObject
            {
                Name = $"Text{i}", Text = pages[i].Text, Width = 600, Height = 50,
                Font = new System.Drawing.Font("Arial", 24)
            });
        }

        Assert.True(report.Prepare());
        return report;
    }

    private static byte[] Export(Report report, FastReport.Export.ExportBase export)
    {
        using var stream = new MemoryStream();
        report.Export(export, stream);
        return stream.ToArray();
    }

    private static List<string> ImageHeaders(byte[] pdf) =>
        Regex.Matches(Encoding.Latin1.GetString(pdf), @"/Subtype /Image[^>]*>>").Select(m => m.Value).ToList();

    private static int PageCount(byte[] pdf) =>
        Regex.Matches(Encoding.Latin1.GetString(pdf), @"/Type\s*/Page[^s]").Count;

    [Fact]
    public void Export_MatchesPdfSimpleExport_PagesAndImageSize()
    {
        using var report = Prepared(("Página um", false), ("Página dois", false), ("Página três", false));

        var original = Export(report, new PDFSimpleExport { ShowProgress = false });
        var reused = Export(report, new ReusableBitmapPdfExport());

        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(reused, 0, 4));
        Assert.Equal(PageCount(original), PageCount(reused));
        Assert.Equal(3, PageCount(reused));
        // Mesma resolução (300 dpi): A4 retrato = 2480 x 3508.
        Assert.All(ImageHeaders(reused), h => Assert.Contains("/Width 2480 /Height 3508", h));
        Assert.InRange(reused.Length, original.Length * 0.8, original.Length * 1.2);
    }

    [Fact]
    public void Export_EachPageHasItsOwnContent()
    {
        // Páginas iguais viram a mesma imagem; diferentes, não — prova que o bitmap é limpo entre páginas.
        using var report = Prepared(("AAAA", false), ("BBBB", false), ("AAAA", false));

        var pdf = Encoding.Latin1.GetString(Export(report, new ReusableBitmapPdfExport()));

        Assert.Equal(2, Regex.Matches(pdf, "/Subtype /Image").Count);
        Assert.Equal(3, PageCount(Encoding.Latin1.GetBytes(pdf)));
    }

    [Fact]
    public void Export_MixedOrientation_UsesPageSizedImages()
    {
        using var report = Prepared(("Retrato", false), ("Paisagem", true), ("Retrato", false));

        var headers = ImageHeaders(Export(report, new ReusableBitmapPdfExport()));

        Assert.Contains(headers, h => h.Contains("/Width 2480 /Height 3508"));
        Assert.Contains(headers, h => h.Contains("/Width 3508 /Height 2480"));
    }

    [Fact]
    public void Export_SetsTitle()
    {
        using var report = Prepared(("x", false));

        static string TitleEntry(byte[] pdf) =>
            Regex.Match(Encoding.Latin1.GetString(pdf), @"/Title\s*(<[^>]*>|\([^)]*\))").Value;

        var original = TitleEntry(Export(report, new PDFSimpleExport { ShowProgress = false, Title = "Extrato" }));
        var reused = TitleEntry(Export(report, new ReusableBitmapPdfExport { Title = "Extrato" }));

        Assert.NotEmpty(original);
        Assert.Equal(original, reused);
    }
}
