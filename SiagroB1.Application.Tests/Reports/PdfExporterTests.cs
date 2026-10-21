using FastReport;
using SiagroB1.Reports.Services;

namespace SiagroB1.Application.Tests.Reports;

/// <summary>
/// The PDF rasterisation leaks ~35 MB per page through libgdiplus on Linux, so the export runs in a
/// short-lived child process. These tests drive the real child (the Reports executable built next to
/// this test project) and compare it with the in-process export.
/// </summary>
[Collection("PdfExporterChild")]
public class PdfExporterTests
{
    static PdfExporterTests()
    {
        FastReport.Utils.RegisteredObjects.AddConnection(typeof(FastReport.Data.MsSqlDataConnection));
        FastReport.Utils.Config.WebMode = true;
    }

    private static string ReportsBuildDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "SiagroB1.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);

        // Mesma configuração/TFM deste projeto de teste (bin/<Config>/<tfm>/).
        var testBin = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        var tfm = testBin.Name;
        var configuration = testBin.Parent!.Name;
        return Path.Combine(dir.FullName, "SiagroB1.Reports", "bin", configuration, tfm);
    }

    private static Report PreparedSample(int pages)
    {
        var report = new Report();
        for (var i = 1; i <= pages; i++)
        {
            var page = new ReportPage { Name = $"Page{i}" };
            report.Pages.Add(page);
            page.ReportTitle = new ReportTitleBand { Name = $"Title{i}", Height = 40 };
            page.ReportTitle.Objects.Add(new TextObject
            {
                Name = $"Text{i}", Text = $"Página {i} — ação", Width = 300, Height = 30,
                Font = new System.Drawing.Font("Arial", 12)
            });
        }

        Assert.True(report.Prepare());
        return report;
    }

    private static int CountPdfPages(byte[] pdf)
    {
        var text = System.Text.Encoding.Latin1.GetString(pdf);
        return System.Text.RegularExpressions.Regex.Matches(text, @"/Type\s*/Page[^s]").Count;
    }

    [Fact]
    public void Isolated_ExportsSamePagesAsInProcess()
    {
        using var report = PreparedSample(3);

        var inProcess = PdfExporter.Export(report, "Amostra", isolate: false);
        var isolated = PdfExporter.Export(report, "Amostra", isolate: true, hostDirectory: ReportsBuildDirectory());

        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(isolated, 0, 4));
        Assert.Equal(3, CountPdfPages(inProcess));
        Assert.Equal(3, CountPdfPages(isolated));
        // Páginas rasterizadas iguais: o tamanho só varia pelos metadados (data de criação).
        Assert.InRange(isolated.Length, inProcess.Length * 0.95, inProcess.Length * 1.05);
    }

    [Fact]
    public void Isolated_LeavesNoTemporaryFiles()
    {
        using var report = PreparedSample(1);
        var before = Directory.GetFiles(Path.GetTempPath(), "siagro-pdf-*").Length;

        PdfExporter.Export(report, null, isolate: true, hostDirectory: ReportsBuildDirectory());

        Assert.Equal(before, Directory.GetFiles(Path.GetTempPath(), "siagro-pdf-*").Length);
    }

    [Fact]
    public void Isolated_ChildFailure_Throws()
    {
        using var report = PreparedSample(1);
        var bogusHost = Path.Combine(Path.GetTempPath(), "siagro-no-such-host-" + Guid.NewGuid());

        Assert.ThrowsAny<Exception>(() =>
            PdfExporter.Export(report, null, isolate: true, hostDirectory: bogusHost));
    }
}
