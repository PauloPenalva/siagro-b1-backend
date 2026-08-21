using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Services.SalesShipmentReleases;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
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

            // No PATCH a entidade chega JÁ rastreada e mutada — o controller a carrega e
            // aplica o Delta nela no MESMO DbContext, então o FirstOrDefault acima devolve
            // essa mesma instância (identity map) e comparar existingEntity com entity
            // compararia o objeto com ele mesmo, nunca acusando mudança. O "antes"
            // verdadeiro está no OriginalValues do rastreador, que serve aos dois caminhos
            // (PATCH aliasado e PUT com entidade desanexada).
            var original = db.Context.Entry(existingEntity).OriginalValues;

            var deliveryChanged =
                (decimal)original[nameof(SalesInvoiceItem.DeliveredQuantity)]! != entity.DeliveredQuantity ||
                (decimal)original[nameof(SalesInvoiceItem.QuantityLoss)]! != entity.QuantityLoss ||
                (SalesInvoiceDeliveryStatus)original[nameof(SalesInvoiceItem.DeliveryStatus)]! != entity.DeliveryStatus;

            // Encerrar com líquido zerado/negativo faria o fator efetivo virar 0 ou negativo
            // e devolver todo o volume ao contrato. Só barra quando a entrega está sendo
            // mexida: linha Closed legada com líquido <= 0 segue editável nos demais campos.
            if (deliveryChanged &&
                entity.DeliveryStatus == SalesInvoiceDeliveryStatus.Closed &&
                entity.DeliveredQuantity - entity.QuantityLoss <= 0)
                throw new DefaultException(
                    "Não é possível encerrar a entrega com peso líquido zerado ou negativo. " +
                    "Informe a quantidade entregue e o desconto antes de encerrar.");

            db.Context.Entry(existingEntity).CurrentValues.SetValues(entity);
            await db.SaveChangesAsync();

            // Entrega/quebra mudou → o fator efetivo do item mudou; recalcula os contratos
            // com alocação neste item no ledger (inclui destinos de realocação).
            if (deliveryChanged)
            {
                await SalesContractsRecalculateBalanceService.RecalculateForItemsAsync(
                    db.Context, [key]);
                await db.SaveChangesAsync();

                // A liberação de entrega segue a MESMA regra do contrato, então precisa do
                // mesmo gatilho. Depois do flush acima: a titularidade da diferença é
                // reeleita em memória lá, e a projeção SQL daqui leria a flag antiga.
                await SalesShipmentReleasesRecalculateShippedService.RecalculateForItemsAsync(
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