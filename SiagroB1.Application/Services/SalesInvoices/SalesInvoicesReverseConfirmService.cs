using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.SalesInvoices;

public class SalesInvoicesReverseConfirmService(
    IUnitOfWork db,
    IStringLocalizer<Resource> resource)
{
    public async Task ExecuteAsync(Guid key, string userName)
    {
        var invoice = await db.Context.SalesInvoices
            .Include(x => x.Items)
            .Include(x => x.SalesTransactions)
            .FirstOrDefaultAsync(x => x.Key == key)
            ?? throw new NotFoundException(
                resource["SALES_INVOICE_NOT_FOUND"].Value);

        if (invoice.InvoiceStatus != InvoiceStatus.Confirmed)
        {
            throw new ApplicationException(
                "Invoice is not confirmed.");
        }

        await db.BeginTransactionAsync();

        try
        {
            if (invoice.InvoiceType == SalesInvoiceType.Return)
            {
                /*
                 * Detecta se devolução é:
                 * NOVA ou LEGADA
                 */
                var isNewFlow = await db.Context.StorageTransactions
                    .AnyAsync(x =>
                        x.ReturnInvoiceKey == invoice.Key);

                if (isNewFlow)
                {
                    await ReverseNewReturnAsync(
                        invoice,
                        userName);
                }
                else
                {
                    await ReverseLegacyReturnAsync(
                        invoice,
                        userName);
                }
            }
            else
            {
                await ReverseNormalInvoiceAsync(
                    invoice,
                    userName);
            }

            invoice.InvoiceStatus = InvoiceStatus.Pending;

            invoice.ApprovedAt = null;
            invoice.ApprovedBy = null;

            await db.SaveChangesAsync();

            await db.CommitAsync();
        }
        catch
        {
            await db.RollbackAsync();
            throw;
        }
    }

    private async Task ReverseNormalInvoiceAsync(
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
                StorageTransactionsStatus.Confirmed;

            transaction.InvoiceNumber = null;
            transaction.InvoiceSerie = null;
            transaction.InvoiceQty = 0;

            transaction.IsInvoiced = false;

            transaction.InvoicedAt = null;

            transaction.UpdatedAt = DateTime.Now;
            transaction.UpdatedBy = userName;
        }
    }

    /*
     * FLUXO NOVO
     */
    private async Task ReverseNewReturnAsync(
        SalesInvoice returnInvoice,
        string userName)
    {
        var transactions = await db.Context.StorageTransactions
            .Where(x =>
                x.ReturnInvoiceKey == returnInvoice.Key)
            .ToListAsync();

        var originInvoice = await db.Context.SalesInvoices
            .Include(x => x.SalesTransactions)
            .FirstOrDefaultAsync(
                x => x.Key == returnInvoice.SalesInvoiceOriginKey)
            ?? throw new ApplicationException(
                "Origin invoice not found.");

        originInvoice.InvoiceStatus = InvoiceStatus.Confirmed;
        originInvoice.UpdatedAt = DateTime.Now;
        originInvoice.UpdatedBy = userName;

        foreach (var transaction in transactions)
        {
            transaction.TransactionStatus =
                StorageTransactionsStatus.Invoiced;

            transaction.ReturnInvoiceKey = null;

            transaction.ReturnedAt = null;
            transaction.ReturnedBy = null;

            transaction.IsInvoiced = true;

            transaction.InvoiceNumber =
                originInvoice.TaxDocumentNumber;

            transaction.InvoiceSerie =
                originInvoice.TaxDocumentSeries;

            transaction.InvoiceQty =
                transaction.NetWeight;

            transaction.UpdatedAt = DateTime.Now;
            transaction.UpdatedBy = userName;

            if (!originInvoice.SalesTransactions
                    .Any(x => x.Key == transaction.Key))
            {
                originInvoice.SalesTransactions
                    .Add(transaction);
            }
        }

        foreach (var item in returnInvoice.Items)
        {
            item.DeliveredQuantity = 0;

            item.DeliveryStatus =
                SalesInvoiceDeliveryStatus.Open;
        }
    }

    /*
     * FLUXO LEGADO
     */
    private async Task ReverseLegacyReturnAsync(
        SalesInvoice returnInvoice,
        string userName)
    {
        var originInvoice = await db.Context.SalesInvoices
            .Include(x => x.SalesTransactions)
            .FirstOrDefaultAsync(
                x => x.Key == returnInvoice.SalesInvoiceOriginKey)
            ?? throw new ApplicationException(
                "Origin invoice not found.");
        
        originInvoice.InvoiceStatus = InvoiceStatus.Confirmed;
        originInvoice.UpdatedAt = DateTime.Now;
        originInvoice.UpdatedBy = userName;
        
        var orphanTransactions =
            await db.Context.StorageTransactions
                .Where(x =>
                    x.SalesInvoiceKey == null &&
                    x.TransactionStatus ==
                        StorageTransactionsStatus.Confirmed &&
                    x.CardCode ==
                        originInvoice.CardCode)
                .ToListAsync();

        var matchedTransactions =
            orphanTransactions
                .Where(x =>
                    returnInvoice.Items.Any(i =>
                        i.ItemCode == x.ItemCode))
                .ToList();

        foreach (var transaction in matchedTransactions)
        {
            transaction.SalesInvoiceKey =
                originInvoice.Key;

            transaction.TransactionStatus =
                StorageTransactionsStatus.Invoiced;

            transaction.InvoiceNumber =
                originInvoice.TaxDocumentNumber;

            transaction.InvoiceSerie =
                originInvoice.TaxDocumentSeries;

            transaction.InvoiceQty =
                transaction.NetWeight;

            transaction.IsInvoiced = true;

            transaction.InvoicedAt ??=
                DateTime.Now;

            transaction.UpdatedAt =
                DateTime.Now;

            transaction.UpdatedBy =
                userName;

            originInvoice.SalesTransactions ??= [];

            if (!originInvoice.SalesTransactions
                    .Any(x => x.Key == transaction.Key))
            {
                originInvoice.SalesTransactions
                    .Add(transaction);
            }
        }

        foreach (var item in returnInvoice.Items)
        {
            item.DeliveredQuantity = 0;

            item.DeliveryStatus =
                SalesInvoiceDeliveryStatus.Open;
        }
    }
}