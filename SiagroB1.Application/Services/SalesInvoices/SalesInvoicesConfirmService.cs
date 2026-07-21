using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using SiagroB1.Application.Services.SalesShipmentReleases;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.SalesInvoices;

public class SalesInvoicesConfirmService(
    IUnitOfWork db,
    SalesShipmentReleasesRecalculateShippedService recalcShipped,
    IStringLocalizer<Resource> resource)
{
    public async Task ExecuteAsync(Guid key, string userName)
    {
        var invoice = await db.Context.SalesInvoices
            .Include(x => x.SalesTransactions)
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Key == key)
            ?? throw new NotFoundException(
                resource["SALES_INVOICE_NOT_FOUND"].Value);

        if (invoice.InvoiceStatus != InvoiceStatus.Pending)
        {
            throw new ApplicationException(
                "Invoice is not pending.");
        }

        await db.BeginTransactionAsync();

        try
        {
            var affectedReleaseKeys = new HashSet<Guid>();

            if (invoice.InvoiceType == SalesInvoiceType.Return)
            {
                await ProcessReturnInvoiceAsync(
                    invoice,
                    userName,
                    affectedReleaseKeys);
            }
            else
            {
                await ProcessNormalInvoiceAsync(
                    invoice,
                    userName);
            }

            invoice.InvoiceStatus = InvoiceStatus.Confirmed;
            invoice.ApprovedBy = userName;
            invoice.ApprovedAt = DateTime.Now;

            await db.SaveChangesAsync();

            // Romaneios da origem viraram Returned → saem da soma da liberação (saldo restaurado).
            foreach (var releaseKey in affectedReleaseKeys)
                await recalcShipped.RecalculateAsync(releaseKey);

            await db.CommitAsync();
        }
        catch
        {
            await db.RollbackAsync();
            throw;
        }
    }

    private async Task ProcessNormalInvoiceAsync(
        SalesInvoice invoice,
        string userName)
    {
        if (invoice.SalesTransactions == null)
        {
            return;
        }

        foreach (var transaction in invoice.SalesTransactions)
        {
            transaction.TransactionStatus =
                StorageTransactionsStatus.Invoiced;

            transaction.InvoiceNumber =
                invoice.TaxDocumentNumber;

            transaction.InvoiceSerie =
                invoice.TaxDocumentSeries;

            transaction.InvoiceQty =
                transaction.NetWeight;

            transaction.IsInvoiced = true;

            transaction.InvoicedAt = DateTime.Now;

            transaction.UpdatedAt = DateTime.Now;
            transaction.UpdatedBy = userName;
        }
    }

    private async Task ProcessReturnInvoiceAsync(
        SalesInvoice returnInvoice,
        string userName,
        HashSet<Guid> affectedReleaseKeys)
    {
        foreach (var item in returnInvoice.Items)
        {
            ValidateLineItemBalance(item);

            item.DeliveredQuantity = item.Quantity;

            item.DeliveryStatus =
                SalesInvoiceDeliveryStatus.Closed;
        }

        var originInvoice = await db.Context.SalesInvoices
            .Include(x => x.SalesTransactions)
            .FirstOrDefaultAsync(
                x => x.Key == returnInvoice.SalesInvoiceOriginKey)
            ?? throw new KeyNotFoundException(
                $"Origin invoice not found.");

        if (originInvoice.InvoiceStatus ==
            InvoiceStatus.Cancelled)
        {
            throw new ApplicationException(
                "Sales invoice origin is cancelled.");
        }

        if (originInvoice.SalesTransactions == null)
        {
            return;
        }

        foreach (var transaction in originInvoice.SalesTransactions)
        {
            if (transaction.SalesShipmentReleaseKey is { } releaseKey)
                affectedReleaseKeys.Add(releaseKey);

            transaction.TransactionStatus =
                StorageTransactionsStatus.Returned;

            transaction.ReturnInvoiceKey =
                returnInvoice.Key;

            transaction.ReturnedAt =
                DateTime.Now;

            transaction.ReturnedBy =
                userName;

            transaction.IsInvoiced = false;

            transaction.InvoiceQty = 0;

            transaction.UpdatedAt = DateTime.Now;
            transaction.UpdatedBy = userName;
        }
    }

    private void ValidateLineItemBalance(
        SalesInvoiceItem item)
    {
        var totalOriginal = db.Context.SalesInvoicesItems
            .AsNoTracking()
            .Where(x => x.Key == item.SalesInvoiceItemOriginKey)
            .Select(x => x.Quantity)
            .SingleOrDefault();

        if (totalOriginal <= 0)
        {
            throw new ApplicationException(
                "Original invoice item not found.");
        }

        var totalIncoming = db.Context.SalesInvoicesItems
            .Where(x =>
                x.SalesInvoice.InvoiceType ==
                    SalesInvoiceType.Return &&

                x.SalesInvoice.InvoiceStatus !=
                    InvoiceStatus.Cancelled &&

                x.SalesInvoiceItemOriginKey ==
                    item.SalesInvoiceItemOriginKey &&

                x.Key != item.Key)
            .Sum(x => x.Quantity);

        if (totalIncoming + item.Quantity > totalOriginal)
        {
            throw new ApplicationException(
                "Returned quantity exceeds the original invoice item quantity.");
        }
    }
}