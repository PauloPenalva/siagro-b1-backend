using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.StorageInvoices;

public class StorageInvoiceOpenService(
    IUnitOfWork db,
    IStringLocalizer<Resource> resource,
    ILogger<StorageInvoiceOpenService> logger)
    : IStorageInvoiceOpenService
{
    public async Task OpenAsync(
        Guid key,
        string userName,
        CancellationToken ct = default)
    {
        var invoice = await db.Context.StorageInvoices
                          .FirstOrDefaultAsync(x => x.Key == key, ct)
                      ?? throw new NotFoundException(resource["STORAGE_INVOICE_NOT_FOUND"]);

        switch (invoice.Status)
        {
            case StorageInvoiceStatus.Cancelled:
                throw new BusinessException(resource["STORAGE_INVOICE_ALREADY_CANCELLED"]);
            case StorageInvoiceStatus.Open:
                throw new BusinessException(resource["STORAGE_INVOICE_ALREADY_OPENED"]);
            case StorageInvoiceStatus.Closed:
            default:
                try
                {
                    invoice.Status = StorageInvoiceStatus.Open;
                    await db.SaveChangesAsync();
                }
                catch (Exception e)
                {
                    throw  new BusinessException(e.Message);
                }

                break;
        }
    }
}