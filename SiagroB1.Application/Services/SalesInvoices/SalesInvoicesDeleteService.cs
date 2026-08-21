using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.SalesInvoices;

public class SalesInvoicesDeleteService(
    IUnitOfWork db,
    ShipmentLoadsBalanceHookService loadHook,
    ILogger<SalesInvoicesDeleteService> logger)
{
    public async Task<bool> ExecuteAsync(Guid key, string userName)
    {
        return await DeleteAsyncWithTransaction(key, userName, async entity =>
        {
            if (entity.InvoiceStatus != InvoiceStatus.Pending)
                throw new ApplicationException($"Entity {nameof(entity)} with ID {entity.Key} is not pending.");
            
            await db.Context.Entry(entity).Collection(e => e.Items).LoadAsync();

            // Excluir um RETORNO tem o mesmo efeito do cancelamento sobre a origem: o
            // documento deixa de existir, então o status "Retornado" e o fechamento da
            // entrega que ele aplicou são desfeitos. No-op para documento normal.
            await SalesInvoicesReturnOriginRestoreService.ExecuteAsync(
                db.Context, entity, userName);

            if (entity.Items.Any())
                db.Context.SalesInvoicesItems.RemoveRange(entity.Items);

            await db.SaveChangesAsync();
        });
    }
    
    private async Task<bool> DeleteAsyncWithTransaction(Guid key, string userName, Func<SalesInvoice, Task>? preDeleteAction = null)
    {
        try
        {
            await db.BeginTransactionAsync();
            var entity = await db.Context.SalesInvoices
                .FirstOrDefaultAsync(x => x.Key == key);
            
            if (entity == null)
            {
                logger.LogWarning("Entity {Entity} with ID {Id} not found.", nameof(SalesContract), key);
                return false;
            }

            
            if (preDeleteAction != null)
                await preDeleteAction(entity);

            db.Context.SalesInvoices.Remove(entity);
            
            await db.SaveChangesAsync();

            // Saldo da carga, DEPOIS do flush do Remove. E ainda com excludedInvoiceKeys como
            // cinto de segurança: a nota removida só some do SumAsync depois do flush, e
            // depender só disso deixaria o resultado à mercê da ordem das operações.
            //
            // Aqui o discriminador tem de ser o ESCALAR ShipmentLoadKey: este serviço não faz
            // Include de SalesTransactions, então SalesInvoiceOriginResolver.Resolve navegaria
            // numa coleção não carregada.
            await loadHook.ApplyAsync(
                entity,
                ShipmentLoadMovementType.BillingDeleted,
                userName,
                $"Documento de saída {entity.InvoiceNumber} excluído.",
                excludedInvoiceKeys: [entity.Key]);

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