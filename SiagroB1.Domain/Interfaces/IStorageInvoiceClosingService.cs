using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Domain.Interfaces;

public interface IStorageInvoiceClosingService
{
    Task<StorageInvoice> CloseAsync(StorageInvoiceCloseRequest request, string userName, CancellationToken ct = default);
}