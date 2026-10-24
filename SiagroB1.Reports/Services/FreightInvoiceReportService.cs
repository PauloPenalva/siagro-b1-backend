using FastReport;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Interfaces;

namespace SiagroB1.Reports.Services;

public class FreightInvoiceReportService(
    IWebHostEnvironment env,
    IFreightInvoicePrintDataService printDataService,
    ReportHeaderService reportHeader)
    : IFreightInvoiceReportService
{
    public async Task<byte[]> GeneratePdfAsync(Guid freightInvoiceKey, CancellationToken ct = default)
    {
        var dto = await printDataService.GetAsync(freightInvoiceKey, ct);

        var reportPath = Path.Combine(env.ContentRootPath, "Reports", "Templates", "FreightInvoice.frx");

        using var report = new Report();
        report.Load(reportPath);
        reportHeader.Apply(report);

        report.RegisterData(new List<FreightInvoicePrintDto> { dto }, "Invoice");
        report.RegisterData(dto.Items, "InvoiceItems");
        report.RegisterData(dto.Installments, "InvoiceInstallments");

        report.GetDataSource("Invoice").Enabled = true;
        report.GetDataSource("InvoiceItems").Enabled = true;
        report.GetDataSource("InvoiceInstallments").Enabled = true;

        report.Prepare();

        return PdfExporter.Export(report, null);
    }
}
