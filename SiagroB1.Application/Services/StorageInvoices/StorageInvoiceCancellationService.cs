using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.StorageInvoices;

public class StorageInvoiceCancellationService(
    IUnitOfWork db,
    IStringLocalizer<Resource> resource,
    ILogger<StorageInvoiceCancellationService> logger)
    : IStorageInvoiceCancellationService
{
    public async Task<StorageInvoice> CancelAsync(
        StorageInvoiceCancelRequest request,
        string userName,
        CancellationToken ct = default)
    {
        if (request.StorageInvoiceKey == Guid.Empty)
            throw new BusinessException(resource["STORAGE_INVOICE_INVALID_KEY"]);

        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new BusinessException(resource["STORAGE_INVOICE_CANCELLATION_REASON_REQUIRED"]);

        var invoice = await db.Context.StorageInvoices
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Key == request.StorageInvoiceKey, ct)
            ?? throw new NotFoundException(resource["STORAGE_INVOICE_NOT_FOUND"]);

        if (invoice.Status == StorageInvoiceStatus.Cancelled)
            throw new BusinessException(resource["STORAGE_INVOICE_ALREADY_CANCELLED"]);

        await db.BeginTransactionAsync();

        try
        {
            var transactionSourceKeys = invoice.Items
                .Where(x => x.SourceType == nameof(StorageTransaction) && x.SourceKey.HasValue)
                .Select(x => x.SourceKey!.Value)
                .Distinct()
                .ToList();

            var chargeSourceKeys = invoice.Items
                .Where(x => x.SourceType == nameof(StorageCharge) && x.SourceKey.HasValue)
                .Select(x => x.SourceKey!.Value)
                .Distinct()
                .ToList();

            if (transactionSourceKeys.Count > 0)
            {
                var transactions = await db.Context.StorageTransactions
                    .Where(x => transactionSourceKeys.Contains(x.Key))
                    .ToListAsync(ct);

                foreach (var tx in transactions)
                {
                    tx.IsInvoiced = false;
                    tx.StorageInvoiceKey = null;
                    tx.InvoicedAt = null;
                }
            }

            if (chargeSourceKeys.Count > 0)
            {
                var charges = await db.Context.StorageCharges
                    .Where(x => chargeSourceKeys.Contains(x.Key))
                    .ToListAsync(ct);

                foreach (var charge in charges)
                {
                    charge.IsInvoiced = false;
                    charge.StorageInvoiceKey = null;
                }
            }

            invoice.Status = StorageInvoiceStatus.Cancelled;
            invoice.CanceledAt = DateTime.Now;
            invoice.CanceledBy = userName;
            invoice.CancellationReason = request.Reason;

            await db.SaveChangesAsync();
            await db.CommitAsync();

            logger.LogInformation(
                "Fatura de armazenagem cancelada. InvoiceKey: {InvoiceKey}, Lote: {StorageAddressCode}, Período: {PeriodStart} a {PeriodEnd}, Usuário: {UserName}",
                invoice.Key,
                invoice.StorageAddressCode,
                invoice.PeriodStart,
                invoice.PeriodEnd,
                userName);

            return invoice;
        }
        catch (Exception ex)
        {
            await db.RollbackAsync();

            logger.LogError(
                ex,
                "Erro ao cancelar fatura de armazenagem. InvoiceKey: {InvoiceKey}, Usuário: {UserName}",
                request.StorageInvoiceKey,
                userName);

            throw;
        }
    }
}