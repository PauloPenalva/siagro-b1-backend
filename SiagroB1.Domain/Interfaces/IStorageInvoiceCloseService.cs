using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Domain.Interfaces;

public interface IStorageInvoiceCloseService
{
    Task CloseAsync(Guid key, string userName, CancellationToken ct = default);
}