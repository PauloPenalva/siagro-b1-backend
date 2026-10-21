using FastReport;
using FastReport.Export.PdfSimple;
using FastReport.Data;
using SiagroB1.Infra.Companies;

namespace SiagroB1.Reports.Services;

public class FastReportService(
    IWebHostEnvironment env,
    IConfiguration configuration,
    ReportHeaderService reportHeader,
    ICompanyContext companyContext) : IFastReportService
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

        sqlConn.ConnectionString = companyContext.ConnectionString;
        
        ApplyParameters(report, parameters);

        if (!await report.PrepareAsync()) return Array.Empty<byte>();
        return PdfExporter.Export(report, reportName);
    }
    
    public async Task<byte[]> GeneratePdfAsync<T>(
        string reportName,
        ICollection<T> data,
        string dataSourceName,
        string refName,
        Dictionary<string, object> parameters)
    {
        using var report = await PrepareAsync(reportName, data, dataSourceName, refName, parameters);
        return report is null ? Array.Empty<byte>() : ExportPdf(report, reportName);
    }

    /// <summary>
    /// Carrega, alimenta e prepara o relatório sem exportar — permite inspecionar as páginas preparadas
    /// (numeração, totais por página). Nulo quando o FastReport não prepara. O chamador descarta.
    /// </summary>
    public async Task<Report?> PrepareAsync<T>(
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
        var report = new Report();
        try
        {
            report.Load(reportPath);
            reportHeader.Apply(report);
            report.RegisterData(data, refName);
            report.GetDataSource(dataSourceName).Enabled = true;
            ApplyParameters(report, parameters);

            if (await report.PrepareAsync())
                return report;
        }
        catch
        {
            report.Dispose();
            throw;
        }

        report.Dispose();
        return null;
    }

    public static byte[] ExportPdf(Report report, string title)
    {
        return PdfExporter.Export(report, title);
    }

}
