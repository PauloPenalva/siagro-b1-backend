using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.SalesInvoices;

public class SalesInvoicesUpdateService(
    IUnitOfWork db, 
    IBusinessPartnerService businessPartnerService,
    SalesInvoicesTaxApplyService taxApply,
    ILogger<SalesInvoicesUpdateService> logger)
{
    public async Task<SalesInvoice?> ExecuteAsync(Guid key, SalesInvoice entity, string userName)
    {
        var existingEntity = await db.Context.SalesInvoices
            .FirstOrDefaultAsync(tc => tc.Key == key) ?? throw new KeyNotFoundException("Entity not found.");
        
        try
        {
            // O "antes" vem do rastreador: no PATCH a entidade chega já mutada (é a mesma
            // instância de existingEntity), então compará-las não acusaria mudança nenhuma.
            var original = db.Context.Entry(existingEntity).OriginalValues;
            var fiscalInputsChanged =
                !Equals(original[nameof(SalesInvoice.InvoiceDate)], entity.InvoiceDate) ||
                !Equals(original[nameof(SalesInvoice.CardCode)], entity.CardCode) ||
                !Equals(original[nameof(SalesInvoice.BranchCode)], entity.BranchCode);

            // Pelo GRAVADO: no PATCH a instância já chega mutada, e um corpo trocando situação, tipo ou filial não pode
            // escapar da trava do documento confirmado.
            var confirmedFrozen = SalesInvoiceNfeLock.IsConfirmedFrozen(
                (InvoiceStatus?)original[nameof(SalesInvoice.InvoiceStatus)],
                (SalesInvoiceType)original[nameof(SalesInvoice.InvoiceType)]!,
                await taxApply.IsBranchActiveAsync((string?)original[nameof(SalesInvoice.BranchCode)]));

            var entry = db.Context.Entry(existingEntity);
            entry.CurrentValues.SetValues(entity);

            SalesInvoiceNfeLock.EnsureHeaderEditable(entry, confirmedFrozen);
            SalesInvoiceNfeLock.RestoreIssuanceFields(entry);
            SalesInvoiceNfeReturnLock.RestoreHeader(entry);

            existingEntity.UpdatedAt = DateTime.Now;
            existingEntity.UpdatedBy = userName;
            existingEntity.CardName = (await businessPartnerService.GetByIdAsync(existingEntity.CardCode))?.CardName;
            existingEntity.TruckingCompanyName =
                entity.TruckingCompanyCode != null
                    ? (await businessPartnerService.GetByIdAsync(entity.TruckingCompanyCode))?.CardName
                    : string.Empty;
            existingEntity.DeliveryCardName =
                entity.DeliveryCardCode != null
                    ? (await businessPartnerService.GetByIdAsync(entity.DeliveryCardCode))?.CardName
                    : string.Empty;

            // Data, cliente e filial são entradas do cálculo (vigência do IBS/CBS, UF, regime):
            // mudou um deles, todas as linhas se recalculam. No-op com a regra inativa.
            if (fiscalInputsChanged)
            {
                var items = await db.Context.SalesInvoicesItems
                    .Where(i => i.SalesInvoiceKey == existingEntity.Key)
                    .ToListAsync();

                await taxApply.ApplyAsync(existingEntity, items);
            }

            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            logger.Log(LogLevel.Error, "Failed to update entity.");
            throw new DefaultException("Error updating entity due to concurrency issues.");
        }

        return entity;
    }
    
}