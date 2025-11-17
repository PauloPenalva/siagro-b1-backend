using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Interfaces.PurchaseContracts;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.PurchaseContracts;

public class PurchaseContractsDeleteService(AppDbContext context, ILogger<PurchaseContractsDeleteService> logger)
{
    public async Task<bool> ExecuteAsync(Guid key)
    {
        return await DeleteAsyncWithTransaction(key, async entity =>
        {
            // Carrega filhos explicitamente
            await context.Entry(entity).Collection(e => e.PriceFixations).LoadAsync();
            await context.Entry(entity).Collection(e => e.QualityParameters).LoadAsync();
            await context.Entry(entity).Collection(e => e.Taxes).LoadAsync();
            
            // Remove filhos primeiro (caso o cascade não esteja funcionando)
            if (entity.PriceFixations.Any())
                context.PurchaseContractsPriceFixations.RemoveRange(entity.PriceFixations);

            if (entity.QualityParameters.Any())
                context.PurchaseContractQualityParameters.RemoveRange(entity.QualityParameters);

            if (entity.Taxes.Any())
                context.PurchaseContractsTaxes.RemoveRange(entity.Taxes);

            await context.SaveChangesAsync();
        });
    }

    private bool EntityExists(Guid key)
    {
        return context.Set<PurchaseContract>().Any(e => e.Key == key);
    }
    
    private async Task<bool> DeleteAsyncWithTransaction(Guid id, Func<PurchaseContract, Task>? preDeleteAction = null)
    {
        await using var transaction = await context.Database.BeginTransactionAsync();
        try
        {
            var entity = await context.PurchaseContracts.FindAsync(id);
            if (entity == null)
            {
                logger.LogWarning("Entity {Entity} with ID {Id} not found.", nameof(PurchaseContract), id);
                return false;
            }

            if (preDeleteAction != null)
                await preDeleteAction(entity);

            context.PurchaseContracts.Remove(entity);
            await context.SaveChangesAsync();

            await transaction.CommitAsync();
            return true;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            logger.LogError(ex, "Error deleting entity {Entity} with ID {Id}", nameof(PurchaseContract), id);
            throw;
        }
    }
}