using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.SalesInvoices;

public class SalesInvoicesItemsUpdateService(
    IUnitOfWork db, 
    IItemService itemService,
    ILogger<SalesInvoicesUpdateService> logger)
{
    public async Task<SalesInvoiceItem?> ExecuteAsync(Guid key, SalesInvoiceItem entity, string userName)
    {
        var existingEntity = await db.Context.SalesInvoicesItems
            .FirstOrDefaultAsync(tc => tc.Key == key) ?? throw new KeyNotFoundException("Entity not found.");
        
        try
        {
            entity.ItemName = (await itemService.GetByIdAsync(entity.ItemCode))?.ItemName;

            var deliveryChanged =
                existingEntity.DeliveredQuantity != entity.DeliveredQuantity ||
                existingEntity.QuantityLoss != entity.QuantityLoss ||
                existingEntity.DeliveryStatus != entity.DeliveryStatus;

            db.Context.Entry(existingEntity).CurrentValues.SetValues(entity);
            await db.SaveChangesAsync();

            // Entrega/quebra mudou → o fator efetivo do item mudou; recalcula os contratos
            // com alocação neste item no ledger (inclui destinos de realocação).
            if (deliveryChanged)
            {
                await SalesContractsRecalculateBalanceService.RecalculateForItemsAsync(
                    db.Context, [key]);
                await db.SaveChangesAsync();
            }
        }
        catch (DbUpdateConcurrencyException)
        {
            logger.Log(LogLevel.Error, "Failed to update entity.");
            throw new DefaultException("Error updating entity due to concurrency issues.");
        }

        return entity;
    }
    
}