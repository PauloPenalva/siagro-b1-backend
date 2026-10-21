using FastReport;
using SiagroB1.Reports.Services;

namespace SiagroB1.Application.Tests.Reports;

public class PdfExporterTests
{
    static PdfExporterTests()
    {
        FastReport.Utils.Config.WebMode = true;
    }

    [Fact]
    public void Export_PreparedReport_ReturnsPdfWithEveryPage()
    {
        using var report = new Report();
        for (var i = 1; i <= 3; i++)
        {
            var page = new ReportPage { Name = $"Page{i}" };
            report.Pages.Add(page);
            page.ReportTitle = new ReportTitleBand { Name = $"Title{i}", Height = 40 };
            page.ReportTitle.Objects.Add(new TextObject { Name = $"Text{i}", Text = $"Página {i}", Width = 300, Height = 30 });
        }
        Assert.True(report.Prepare());

        var pdf = PdfExporter.Export(report, "Amostra");

        var text = System.Text.Encoding.Latin1.GetString(pdf);
        Assert.StartsWith("%PDF", text);
        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(text, @"/Type\s*/Page[^s]").Count);
    }
}
