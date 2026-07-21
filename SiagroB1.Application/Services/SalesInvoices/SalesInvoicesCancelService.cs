using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.SalesShipmentReleases;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.SalesInvoices;

public class SalesInvoicesCancelService(
    IUnitOfWork db,
    SalesShipmentReleasesRecalculateShippedService recalcShipped,
    ILogger<SalesInvoicesCancelService> logger)
{
    public async Task ExecuteAsync(Guid key, string userName)
    {
        var existingInvoice = await db.Context.SalesInvoices
                                  .Include(e => e.SalesTransactions)
                                  .FirstOrDefaultAsync(x => x.Key == key) ??
                                    throw new KeyNotFoundException($"Key {key} not found");

        if (existingInvoice.InvoiceStatus == InvoiceStatus.Cancelled)
            throw new ApplicationException("Documento já está cancelado.");
        
        if (existingInvoice.InvoiceType == SalesInvoiceType.Return && existingInvoice.InvoiceStatus == InvoiceStatus.Confirmed)
            throw new ApplicationException("Documento do tipo retorno já está confirmado. Não é possivel cancelar.");
        
        if (HasReturn(existingInvoice))
            throw  new ApplicationException("Documento de saída possui retorno.");
        
        var salesTransactionsKeys = existingInvoice.SalesTransactions?.Select(x => x.Key)
            .ToList() ?? [];
        
        existingInvoice.SalesTransactions?.Clear();

        // Liberações de venda afetadas: coletar antes de limpar a chave para recalcular depois.
        var affectedReleaseKeys = new HashSet<Guid>();

        try
        {
            await db.BeginTransactionAsync();

            foreach (var salesTransactionsKey in salesTransactionsKeys)
            {
                var salesTransaction = await db.Context.StorageTransactions
                    .FirstOrDefaultAsync(x => x.Key == salesTransactionsKey);

                if (salesTransaction != null)
                {
                    if (salesTransaction.SalesShipmentReleaseKey is { } releaseKey)
                        affectedReleaseKeys.Add(releaseKey);

                    salesTransaction.TransactionStatus = StorageTransactionsStatus.Confirmed;
                    salesTransaction.InvoiceQty = 0;
                    salesTransaction.InvoiceSerie = string.Empty;
                    salesTransaction.InvoiceNumber = string.Empty;
                    salesTransaction.UpdatedAt = DateTime.Now;
                    salesTransaction.UpdatedBy = userName;
                    salesTransaction.SalesInvoiceKey = null;
                    salesTransaction.SalesShipmentReleaseKey = null;

                    await db.SaveChangesAsync();
                }
            }

            existingInvoice.InvoiceStatus = InvoiceStatus.Cancelled;

            await db.SaveChangesAsync();

            // Romaneios voltaram a Confirmed e perderam a chave → o saldo liberado é restaurado.
            foreach (var releaseKey in affectedReleaseKeys)
                await recalcShipped.RecalculateAsync(releaseKey);

            await db.CommitAsync();
        }
        catch (Exception e)
        {
            await db.RollbackAsync();
            logger.LogError(e.Message);
            throw new ApplicationException(e.Message);
        }
        
    }

    private bool HasReturn(SalesInvoice salesInvoice)
    {
        return db.Context.SalesInvoices.Any(x => x.SalesInvoiceOriginKey == salesInvoice.Key &&
                                                 x.InvoiceType == SalesInvoiceType.Return && x.InvoiceStatus != InvoiceStatus.Cancelled);
    }
}