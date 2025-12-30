namespace SiagroB1.Reports.Services;

using FastReport;
using FastReport.Export.PdfSimple;
using FastReport.Data;

public class FastReportService(
    IWebHostEnvironment env,
    IConfiguration configuration) : IFastReportService
{
    
    public async Task<byte[]> GeneratePdfAsync(
        string reportName,
        Dictionary<string, object> parameters)
    {
        var reportPath = Path.Combine(
            env.ContentRootPath,
            "Reports",
            reportName);

        using var report = new Report();
        report.Load(reportPath);
        
        var sqlConn = report.Dictionary.Connections
            .OfType<MsSqlDataConnection>()
            .FirstOrDefault();

        if (sqlConn == null)
            throw new InvalidOperationException("MsSql connection not found in report.");

        sqlConn.ConnectionString =
            configuration.GetConnectionString("SiagroDB");

        // PASSA PARÂMETROS (igual Jasper)
        foreach (var param in parameters)
        {
            report.SetParameterValue(param.Key, param.Value);
        }

        report.Prepare();

        using var stream = new MemoryStream();
        var pdfExport = new PDFSimpleExport();
        report.Export(pdfExport, stream);

        return stream.ToArray();
    }

}
