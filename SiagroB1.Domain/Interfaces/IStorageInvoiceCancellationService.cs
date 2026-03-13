using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Domain.Interfaces;

public interface IStorageInvoiceCancellationService
{
    Task<StorageInvoice> CancelAsync(StorageInvoiceCancelRequest request, string userName, CancellationToken ct = default);
}