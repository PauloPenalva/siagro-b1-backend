namespace SiagroB1.Reports.Interfaces;

public interface IFreightInvoiceReportService
{
    Task<byte[]> GeneratePdfAsync(Guid freightInvoiceKey, CancellationToken ct = default);
}
