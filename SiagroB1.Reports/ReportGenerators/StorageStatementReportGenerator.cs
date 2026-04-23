using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Interfaces;

namespace SiagroB1.Reports.ReportGenerators;

using FastReport;
using FastReport.Export.PdfSimple;

public class StorageStatementReportGenerator(
    IStorageStatementReportService reportService,
    IWebHostEnvironment environment)
{
    public async Task<byte[]> GeneratePdfAsync(
        StorageStatementReportFilter filter,
        CancellationToken cancellationToken = default)
    {
        var data = await reportService.GetReportAsync(filter, cancellationToken);

        var reportPath = Path.Combine(
            environment.ContentRootPath,
            "Reports",
            "Templates",
            "StorageStatement.frx");

        using var report = new Report();
        report.Load(reportPath);

        report.RegisterData(data.rows, "Statement");
        //report.RegisterData(data.header, "Header");
        
        var ds = report.GetDataSource("Statement");
        if (ds != null)
            ds.Enabled = true;

        report.SetParameterValue("ReportTitle", "Extrato de Entrada e Saída");
        report.SetParameterValue("Period", BuildPeriod(filter));
        report.SetParameterValue("Branch", filter.BranchCode ?? "Todas");
        report.SetParameterValue("StorageAddress", filter.StorageAddressCode ?? "Todos");
        report.SetParameterValue("Customer", filter.CardCode ?? "Todos");
        report.SetParameterValue("Product", filter.ItemCode ?? "Todos");
        report.SetParameterValue("Warehouse", filter.WarehouseCode ?? "Todos");
        report.SetParameterValue("GeneratedAt", DateTime.Now.ToString("dd/MM/yyyy HH:mm"));

        report.Prepare();

        using var ms = new MemoryStream();
        using var export = new PDFSimpleExport();
        report.Export(export, ms);

        return ms.ToArray();
    }

    private static string BuildPeriod(StorageStatementReportFilter filter)
    {
        var start = filter.DateStart?.ToString("dd/MM/yyyy") ?? "...";
        var end = filter.DateEnd?.ToString("dd/MM/yyyy") ?? "...";
        return $"{start} a {end}";
    }
}