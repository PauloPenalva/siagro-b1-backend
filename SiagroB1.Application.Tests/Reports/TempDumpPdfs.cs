using FastReport;
using FastReport.Export.PdfSimple;
using Microsoft.Extensions.Configuration;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Reports.Services;

namespace SiagroB1.Application.Tests.Reports;

public class TempDumpPdfs
{
    [Fact]
    public void Dump()
    {
        FastReport.Utils.RegisteredObjects.AddConnection(typeof(FastReport.Data.MsSqlDataConnection));
        FastReport.Utils.Config.WebMode = true;

        var outDir = @"C:\Users\Penalva\AppData\Local\Temp\claude\C--Projetos-SiagroB1\8bdb1d9a-753d-48e2-8ede-11ba0e52bfea\scratchpad\pdfs";
        Directory.CreateDirectory(outDir);

        var contentRoot = Path.Combine(AppContext.BaseDirectory, "ReportsContentRoot");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CompanyName"] = "COMERCIO DE CEREAIS YOKOTOBI LTDA",
                ["CompanyLogoPath"] = "wwwroot/images/logo.jpeg"
            })
            .Build();

        var svc = new ReportHeaderService(new TestWebHostEnvironment(contentRoot), configuration,
            new TestLogger<ReportHeaderService>());

        foreach (var name in ReportTemplateHeaderTests.ProductionTemplateNames())
        {
            if (name == "WeighingTicket.frx") continue;
            using var report = new Report();
            report.Load(Path.Combine(AppContext.BaseDirectory, "ReportTemplates", name));
            svc.Apply(report);
            report.Prepare();
            using var fs = File.Create(Path.Combine(outDir, Path.ChangeExtension(name, ".pdf")));
            report.Export(new PDFSimpleExport { ShowProgress = false }, fs);
        }
    }
}
