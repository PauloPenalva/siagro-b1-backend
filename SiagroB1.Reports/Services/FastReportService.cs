using FastReport;
using FastReport.Export.PdfSimple;
using FastReport.Data;

namespace SiagroB1.Reports.Services;

public class FastReportService(
    IWebHostEnvironment env,
    IConfiguration configuration,
    ReportHeaderService reportHeader) : IFastReportService
{
    /// <summary>
    /// Parâmetro reservado: com <c>false</c> (modo SAPB1) os objetos cujo nome começa com
    /// "fiscal" somem do PDF — tributos e situação da NF-e não são calculados pelo Siagro nesse modo.
    /// </summary>
    public const string StandaloneParameter = "pStandalone";

    /// <summary>
    /// Parâmetro reservado dos relatórios agrupados por produto + UM: com <c>true</c> (uma só UM no
    /// resultado) some a nota "UMs diferentes" (<c>uomMixed*</c>); com <c>false</c> somem os totais
    /// gerais de quantidade (<c>uomSum*</c>) — KG e TN não se somam. Ausente, nada muda.
    /// </summary>
    public const string SingleUomParameter = "pSingleUom";
    public const string UomTotalsPrefix = "uomSum";
    public const string MixedUomNotePrefix = "uomMixed";

    public static void HideFiscalObjects(Report report) => HideObjectsWithPrefix(report, "fiscal");

    public static void ApplySingleUom(Report report, bool singleUom) =>
        HideObjectsWithPrefix(report, singleUom ? MixedUomNotePrefix : UomTotalsPrefix);

    private static void HideObjectsWithPrefix(Report report, string prefix)
    {
        foreach (var component in report.AllObjects.OfType<ReportComponentBase>())
        {
            if (component.Name.StartsWith(prefix, StringComparison.Ordinal))
                component.Visible = false;
        }
    }

    private static void ApplyParameters(Report report, Dictionary<string, object> parameters)
    {
        foreach (var param in parameters)
            report.SetParameterValue(param.Key, param.Value);

        if (parameters.TryGetValue(StandaloneParameter, out var standalone) && standalone is false)
            HideFiscalObjects(report);

        if (parameters.TryGetValue(SingleUomParameter, out var single) && single is bool singleUom)
            ApplySingleUom(report, singleUom);
    }

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
        reportHeader.Apply(report);

        var sqlConn = report.Dictionary.Connections
            .OfType<MsSqlDataConnection>()
            .FirstOrDefault();

        if (sqlConn == null)
            throw new InvalidOperationException("MsSql connection not found in report.");

        sqlConn.ConnectionString =
            configuration.GetConnectionString("SiagroDB");
        
        ApplyParameters(report, parameters);

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
        reportHeader.Apply(report);

        report.RegisterData(data, refName);

        report.GetDataSource(dataSourceName).Enabled = true;
        
        ApplyParameters(report, parameters);

        if (!await report.PrepareAsync()) return Array.Empty<byte>();
        var pdfExport = new PDFSimpleExport();
        pdfExport.ShowProgress = false;
        pdfExport.Title = reportName;
            
        using var stream = new MemoryStream();
            
        report.Export(pdfExport, stream);

        return stream.ToArray();
    }
    
    
}
