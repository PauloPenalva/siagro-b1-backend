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
            "Templates",
            reportName);

        FastReport.Utils.Config.WebMode = true;
        using var report = new Report();
        report.Load(reportPath);
        
        var sqlConn = report.Dictionary.Connections
            .OfType<MsSqlDataConnection>()
            .FirstOrDefault();

        if (sqlConn == null)
            throw new InvalidOperationException("MsSql connection not found in report.");

        sqlConn.ConnectionString =
            configuration.GetConnectionString("SiagroDB");
        
        foreach (var param in parameters)
        {
            report.SetParameterValue(param.Key, param.Value);
        }

        if (!await report.PrepareAsync()) return Array.Empty<byte>();
        var pdfExport = new PDFSimpleExport();
        pdfExport.ShowProgress = false;
        pdfExport.Title = reportName;
            
        using var stream = new MemoryStream();
            
        report.Export(pdfExport, stream);

        return stream.ToArray();
    }
    
    public async Task<byte[]> GeneratePdfAsync<T>(
        string reportName,
        ICollection<T> data,
        string dataSourceName,
        string refName,
        Dictionary<string, object> parameters)
    {
        var reportPath = Path.Combine(
            env.ContentRootPath,
            "Reports",
            "Templates",
            reportName);

        FastReport.Utils.Config.WebMode = true;
        using var report = new Report();
        report.Load(reportPath);
        
        report.RegisterData(data, refName);

        report.GetDataSource(dataSourceName).Enabled = true;
        
        foreach (var param in parameters)
        {
            report.SetParameterValue(param.Key, param.Value);
        }

        if (!await report.PrepareAsync()) return Array.Empty<byte>();
        var pdfExport = new PDFSimpleExport();
        pdfExport.ShowProgress = false;
        pdfExport.Title = reportName;
            
        using var stream = new MemoryStream();
            
        report.Export(pdfExport, stream);

        return stream.ToArray();
    }
}
