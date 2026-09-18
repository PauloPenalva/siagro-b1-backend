using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.SalesInvoices;

public class SalesInvoicesItemsDeleteService(IUnitOfWork db, ILogger<SalesInvoicesItemsDeleteService> logger)
{
    public async Task<bool> ExecuteAsync(Guid key)
    {
        return await DeleteAsyncWithTransaction(key, async entity =>
        {
            await db.SaveChangesAsync();
        });
    }
    
    private async Task<bool> DeleteAsyncWithTransaction(Guid key, Func<SalesInvoiceItem, Task>? preDeleteAction = null)
    {
        try
        {
            await db.BeginTransactionAsync();
            var entity = await db.Context.SalesInvoicesItems
                .FirstOrDefaultAsync(x => x.Key == key);
            
            if (entity == null)
            {
                logger.LogWarning("Entity {Entity} with ID {Id} not found.", nameof(SalesContract), key);
                return false;
            }

            
            if (preDeleteAction != null)
                await preDeleteAction(entity);

            var salesInvoiceKey = entity.SalesInvoiceKey;

            // GAC-1171: a FK de SHIPMENT_LOAD_DISCHARGES para a linha da nota é NoAction, de
            // propósito — o ticket de descarga é a evidência física que libera o pagamento do
            // frete e não pode ser levado embora junto com a linha. Sem este guard o banco
            // devolve erro 547, a transação rola atrás e o usuário vê um 500 de corpo vazio.
            // ⚠️ O InMemory dos testes não aplica FK: só o guard faz o caminho aparecer.
            if (await db.Context.ShipmentLoadsDischarges.AnyAsync(x => x.SalesInvoiceItemKey == key))
                throw new DefaultException(
                    "Este item tem ticket de descarga registrado na carga. " +
                    "Exclua o registro de descarga antes.");

            db.Context.SalesInvoicesItems.Remove(entity);

            await db.SaveChangesAsync();

            // Numa devolução o peso do cabeçalho é a soma das linhas. Depois do flush: a soma
            // agrega no SERVIDOR e ainda contaria a linha removida.
            await SalesInvoicesReturnWeightService.RecalculateAsync(db.Context, salesInvoiceKey);

            await db.SaveChangesAsync();
            await db.CommitAsync();
            return true;
        }
        catch (Exception ex)
        {
            await db.RollbackAsync();
            logger.LogError(ex, "Error deleting entity {Entity} with ID {Id}", nameof(SalesContract), key);
            throw;
        }
    }
}