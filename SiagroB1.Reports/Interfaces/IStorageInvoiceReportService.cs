namespace SiagroB1.Reports.Interfaces;

public interface IStorageInvoiceReportService
{
    Task<byte[]> GeneratePdfAsync(Guid storageInvoiceKey, CancellationToken ct = default);
}