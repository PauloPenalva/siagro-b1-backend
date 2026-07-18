using FastReport;
using FastReport.Export.PdfSimple;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Interfaces;

namespace SiagroB1.Reports.Services;

public class StorageInvoiceReportService(
    IWebHostEnvironment env,
    IStorageInvoicePrintDataService printDataService,
    ReportHeaderService reportHeader)
    : IStorageInvoiceReportService
{
    public async Task<byte[]> GeneratePdfAsync(Guid storageInvoiceKey, CancellationToken ct = default)
    {
        var dto = await printDataService.GetAsync(storageInvoiceKey, ct);

        var reportPath = Path.Combine(
            env.ContentRootPath,
            "Reports",
            "Templates",
            "StorageInvoice.frx");

        using var report = new Report();
        report.Load(reportPath);
        reportHeader.Apply(report);

        report.RegisterData(new List<StorageInvoicePrintDto> { dto }, "Invoice");
        report.RegisterData(dto.Items, "InvoiceItems");

        var invoiceDataSource = report.GetDataSource("Invoice");
        invoiceDataSource.Enabled = true;

        var itemsDataSource = report.GetDataSource("InvoiceItems");
        itemsDataSource.Enabled = true;

        report.Prepare();

        using var ms = new MemoryStream();
        using var pdf = new PDFSimpleExport();
        report.Export(pdf, ms);

        return ms.ToArray();
    }
}