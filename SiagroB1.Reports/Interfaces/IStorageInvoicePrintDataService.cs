using SiagroB1.Reports.Dtos;

namespace SiagroB1.Reports.Interfaces;

public interface IStorageInvoicePrintDataService
{
    Task<StorageInvoicePrintDto> GetAsync(Guid storageInvoiceKey, CancellationToken ct = default);
}